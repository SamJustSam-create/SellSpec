using System.Runtime.InteropServices;

namespace SellSpec.Windows.Audio;

/// <summary>
/// Reads the default microphone through the classic waveIn API and reports an input level. Audio buffers live
/// only in memory and are overwritten continuously; nothing is written to disk or kept after <see cref="Stop"/>.
/// </summary>
public sealed class MicrophoneLevelMeter : IDisposable
{
    private const int SampleRate = 16_000;
    private const int BufferCount = 4;
    private const int BufferBytes = SampleRate * 2 / 10; // 100 ms of 16-bit mono
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint CallbackFunction = 0x00030000;
    private const uint MM_WIM_DATA = 0x3C0;
    private const int WHDR_DONE = 0x00000001;

    private readonly WaveInProc _callback;
    private readonly object _gate = new();
    private IntPtr _handle;
    private IntPtr[] _headers = Array.Empty<IntPtr>();
    private IntPtr[] _buffers = Array.Empty<IntPtr>();
    private bool _running;
    private bool _disposed;

    /// <summary>Raised on a system thread with the RMS level (0..1) and peak level (0..1) of the latest 100 ms.</summary>
    public event Action<float, float>? LevelChanged;

    public MicrophoneLevelMeter()
    {
        _callback = OnWaveInMessage;
    }

    public bool IsRunning => _running;

    /// <summary>Starts capture. Returns null on success or a user-readable reason on failure.</summary>
    public string? Start()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MicrophoneLevelMeter));
            if (_running) return null;

            if (waveInGetNumDevs() == 0)
            {
                return "No microphone was found.";
            }

            var format = new WAVEFORMATEX
            {
                wFormatTag = 1,
                nChannels = 1,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 2,
                nAvgBytesPerSec = SampleRate * 2,
                cbSize = 0,
            };

            var result = waveInOpen(out _handle, WaveMapper, ref format, _callback, IntPtr.Zero, CallbackFunction);
            if (result != 0)
            {
                _handle = IntPtr.Zero;
                return DescribeError(result);
            }

            _headers = new IntPtr[BufferCount];
            _buffers = new IntPtr[BufferCount];
            for (var i = 0; i < BufferCount; i++)
            {
                _buffers[i] = Marshal.AllocHGlobal(BufferBytes);
                var header = new WAVEHDR { lpData = _buffers[i], dwBufferLength = BufferBytes, dwUser = (IntPtr)i };
                _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                Marshal.StructureToPtr(header, _headers[i], false);
                waveInPrepareHeader(_handle, _headers[i], (uint)Marshal.SizeOf<WAVEHDR>());
                waveInAddBuffer(_handle, _headers[i], (uint)Marshal.SizeOf<WAVEHDR>());
            }

            result = waveInStart(_handle);
            if (result != 0)
            {
                ReleaseUnsafe();
                return DescribeError(result);
            }

            _running = true;
            return null;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            waveInStop(_handle);
            waveInReset(_handle);
            ReleaseUnsafe();
        }
    }

    private void ReleaseUnsafe()
    {
        if (_handle != IntPtr.Zero)
        {
            for (var i = 0; i < _headers.Length; i++)
            {
                if (_headers[i] != IntPtr.Zero)
                {
                    waveInUnprepareHeader(_handle, _headers[i], (uint)Marshal.SizeOf<WAVEHDR>());
                }
            }
            waveInClose(_handle);
            _handle = IntPtr.Zero;
        }
        for (var i = 0; i < _headers.Length; i++)
        {
            if (_headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_headers[i]);
            if (_buffers[i] != IntPtr.Zero)
            {
                // Zero the capture memory before releasing it.
                var zero = new byte[BufferBytes];
                Marshal.Copy(zero, 0, _buffers[i], BufferBytes);
                Marshal.FreeHGlobal(_buffers[i]);
            }
        }
        _headers = Array.Empty<IntPtr>();
        _buffers = Array.Empty<IntPtr>();
    }

    private void OnWaveInMessage(IntPtr hwi, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (uMsg != MM_WIM_DATA) return;
        lock (_gate)
        {
            if (!_running || dwParam1 == IntPtr.Zero) return;
            var header = Marshal.PtrToStructure<WAVEHDR>(dwParam1);
            if ((header.dwFlags & WHDR_DONE) != 0 && header.dwBytesRecorded > 1)
            {
                var sampleCount = (int)header.dwBytesRecorded / 2;
                double sumSquares = 0;
                var peak = 0;
                for (var i = 0; i < sampleCount; i++)
                {
                    var sample = Marshal.ReadInt16(header.lpData, i * 2);
                    var abs = Math.Abs((int)sample);
                    if (abs > peak) peak = abs;
                    sumSquares += (double)sample * sample;
                }
                var rms = (float)(Math.Sqrt(sumSquares / sampleCount) / short.MaxValue);
                LevelChanged?.Invoke(Math.Min(1f, rms), Math.Min(1f, peak / (float)short.MaxValue));
            }

            header.dwFlags &= unchecked((uint)~WHDR_DONE);
            header.dwBytesRecorded = 0;
            Marshal.StructureToPtr(header, dwParam1, false);
            waveInAddBuffer(_handle, dwParam1, (uint)Marshal.SizeOf<WAVEHDR>());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    private static string DescribeError(uint code) => code switch
    {
        4 => "The microphone is in use by another application.", // MMSYSERR_ALLOCATED
        2 => "No microphone device is available.", // MMSYSERR_BADDEVICEID
        32 => "The microphone does not support the requested format.", // WAVERR_BADFORMAT
        8 => "Audio capture is not available on this system.", // MMSYSERR_NODRIVER
        _ => $"The microphone could not be opened (error {code}). Check Windows microphone privacy settings.",
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private delegate void WaveInProc(IntPtr hwi, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr phwi, uint uDeviceID, ref WAVEFORMATEX pwfx, WaveInProc dwCallback, IntPtr dwInstance, uint fdwOpen);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr hwi, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern uint waveInStop(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr hwi);
}
