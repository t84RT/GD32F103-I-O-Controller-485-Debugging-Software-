using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IOModbusTester
{
    public sealed class IoSnapshot
    {
        public bool Connected, Polling, Demo, HasInputs, HasOutputs, HasAnalog, HasRegisters;
        public bool[] X, Y;
        public ushort[] Analog, Registers, Diagnostics;
        public bool? DiagnosticsSupported;
        public long Tx, Rx, Errors, PollCycles, GoodCycles, LastResponseAgeMs, LastIoAgeMs, ElapsedMs;
        public double LastLatencyMs, MaxLatencyMs;
        public string LastError;
    }

    // Exactly one worker owns all serial operations and mutable device buffers.
    // The UI receives copies, never touches the port, and never waits synchronously for a reply.
    public sealed class IoSession
    {
        private readonly IModbusClient _client;
        private readonly object _gate = new object();
        private readonly BlockingCollection<Action> _commands = new BlockingCollection<Action>(64);
        private readonly ConcurrentQueue<string> _logs = new ConcurrentQueue<string>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly bool[] _x = new bool[64], _y = new bool[64];
        private readonly ushort[] _analog = new ushort[6], _regs = new ushort[19], _diag = new ushort[16];
        private Task _worker;
        private int _intervalMs = 100, _allOff, _stopping, _released;
        private volatile bool _connected, _polling, _demo;
        private bool _hasX, _hasY, _hasA, _hasR;
        private bool? _diagSupported;
        private long _tx, _rx, _errors, _cycles, _goodCycles, _lastResponse = -1, _lastIo = -1, _requestStart;
        private double _lastLatency, _maxLatency;
        private string _lastError = "";
        private bool _capture;
        private volatile bool _traceFrames;
        public bool TraceFrames { get => _traceFrames; set => _traceFrames = value; }
        public bool Polling { get => _polling; set => _polling = value; }
        public bool Demo { get => _demo; set => _demo = value; }
        public int IntervalMs { get => Volatile.Read(ref _intervalMs); set => Volatile.Write(ref _intervalMs, Math.Max(50, Math.Min(5000, value))); }

        public IoSession(IModbusClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.FrameSent += OnSent;
            _client.FrameReceived += OnReceived;
            _client.FrameObserved += OnObserved;
            _client.CommError += OnError;
        }
        private void OnSent(string hex)
        {
            lock (_gate) { _tx++; _requestStart = _clock.ElapsedTicks; }
            if (_capture || TraceFrames) Log("TX " + hex);
        }
        private void OnReceived(string hex)
        {
            lock (_gate)
            {
                _rx++; _lastResponse = _clock.ElapsedMilliseconds;
                _lastLatency = (_clock.ElapsedTicks - _requestStart) * 1000.0 / Stopwatch.Frequency;
                _maxLatency = Math.Max(_maxLatency, _lastLatency);
            }
        }
        private void OnObserved(string hex)
        {
            if (_capture || TraceFrames) Log("RX(raw) " + hex);
        }
        private void OnError(string text)
        {
            bool changed;
            lock (_gate) { _errors++; changed = _lastError != text; _lastError = text; }
            if (changed || _capture) Log("错误: " + text);
        }
        public void Log(string text)
        {
            _logs.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
            while (_logs.Count > 500) _logs.TryDequeue(out _);
        }
        public bool TryTakeLog(out string line) => _logs.TryDequeue(out line);

        public async Task StartAsync()
        {
            if (_worker != null) throw new InvalidOperationException("会话不能重复启动");
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _worker = Task.Run(() => Run(ready));
            await ready.Task.ConfigureAwait(false);
        }
        public async Task StopAsync()
        {
            _polling = false; _demo = false;
            if (Interlocked.Exchange(ref _stopping, 1) == 0) _stop.Cancel();
            if (_worker != null) await _worker.ConfigureAwait(false);
            if (Interlocked.Exchange(ref _released, 1) == 0) { _commands.Dispose(); _stop.Dispose(); }
        }
        private void Run(TaskCompletionSource<bool> ready)
        {
            try
            {
                _client.Open(); _connected = true;
                ready.TrySetResult(true);
                Log("串口已打开，等待有效应答；所有写操作均需人工触发。");
                long nextPoll = 0, nextDemo = 0, nextDiag = 0;
                int shift = 0;
                while (!_stop.IsCancellationRequested)
                {
                    if (Interlocked.Exchange(ref _allOff, 0) != 0)
                    {
                        while (_commands.TryTake(out _)) { }
                        SetOutputs(false);
                    }
                    if (_commands.TryTake(out Action command, 20, _stop.Token)) command();
                    _stop.Token.ThrowIfCancellationRequested();
                    long now = _clock.ElapsedMilliseconds;
                    if (_demo && now >= nextDemo)
                    {
                        bool[] pattern = new bool[IoModuleMap.PhysicalOutputs];
                        int channel = 0;
                        for (int i = 0; i < pattern.Length; i++)
                            if (i != IoModuleMap.CommunicationLedOutput)
                                pattern[i] = ((shift + channel++) % IoModuleMap.ControllableOutputs) < 3;
                        if (!WriteControlledOutputs(pattern, true) || !ReadOutputs())
                        { _demo = false; Log("演示因写入/读回失败或停止请求而停止；部分输出可能已改变，不自动重试。"); }
                        else shift = (shift + 1) % IoModuleMap.ControllableOutputs;
                        nextDemo = _clock.ElapsedMilliseconds + 150;
                    }
                    if (_polling && now >= nextPoll)
                    {
                        PollOnce();
                        nextPoll = _clock.ElapsedMilliseconds + IntervalMs;
                    }
                    if (_polling && _hasX && _clock.ElapsedMilliseconds >= nextDiag && _diagSupported != false)
                    {
                        ReadDiagnostics();
                        nextDiag = _clock.ElapsedMilliseconds + 1000;
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex) { ready.TrySetException(ex); OnError(ex.Message); }
            finally
            {
                _connected = false; _polling = false; _demo = false;
                try { _client.Dispose(); } catch (Exception ex) { Log("释放串口: " + ex.Message); }
                ready.TrySetCanceled();
                Log("会话已停止，旧会话不会再发送请求。");
            }
        }
        private void PollOnce()
        {
            bool[] x = new bool[64], y = new bool[64]; ushort[] a = new ushort[6], r = new ushort[19];
            bool okX = _client.ReadDiscreteInputs(0, 64, x, 0); _stop.Token.ThrowIfCancellationRequested();
            bool okY = _client.ReadCoils(0, 64, y, 0); _stop.Token.ThrowIfCancellationRequested();
            bool okA = _client.ReadInputRegisters(0, 6, a, 0); _stop.Token.ThrowIfCancellationRequested();
            bool okR = _client.ReadHoldingRegisters(0, 19, r, 0);
            lock (_gate)
            {
                if (okX) { Array.Copy(x, _x, 64); _hasX = true; }
                if (okY) { Array.Copy(y, _y, 64); _hasY = true; }
                if (okA) { Array.Copy(a, _analog, 6); _hasA = true; }
                if (okR) { Array.Copy(r, _regs, 19); _hasR = true; }
                _cycles++; if (okX && okY && okA && okR) { _goodCycles++; _lastIo = _clock.ElapsedMilliseconds; }
            }
        }
        private void ReadDiagnostics()
        {
            ushort[] data = new ushort[16];
            bool ok = _client.ReadInputRegisters(IoModuleMap.DiagnosticAddress, 16, data, 0);
            lock (_gate)
            {
                // Only a valid, recognized block enables diagnostics. A transient
                // timeout must not permanently misclassify a new board as legacy.
                if (ok && data[0] == IoModuleMap.DiagnosticSignature) { _diagSupported = true; Array.Copy(data, _diag, 16); }
                else if (ok || _client.LastExceptionCode == 2) _diagSupported = false;
            }
        }
        private bool ReadOutputs()
        {
            bool[] y = new bool[64];
            if (!_client.ReadCoils(0, 64, y, 0)) return false;
            lock (_gate) { Array.Copy(y, _y, 64); _hasY = true; }
            return true;
        }
        private void Queue(Action command)
        {
            if (!_connected || _stop.IsCancellationRequested) throw new InvalidOperationException("串口未连接");
            if (!_commands.TryAdd(command)) throw new InvalidOperationException("操作队列已满，请等待前面的操作完成");
        }
        public void ToggleOutput(int index)
        {
            if (index < 0 || index >= 32) throw new ArgumentOutOfRangeException(nameof(index));
            if (index == IoModuleMap.CommunicationLedOutput) throw new InvalidOperationException("Y17为485通讯专用灯，不允许手动控制");
            if (_demo) throw new InvalidOperationException("请先停止跑马灯演示");
            Queue(() =>
            {
                bool[] current = new bool[1];
                if (!_client.ReadCoils((ushort)index, 1, current, 0)) return;
                _stop.Token.ThrowIfCancellationRequested();
                if (_client.WriteSingleCoil((ushort)index, !current[0])) { Log("写 Y" + index + "，正在读回实际值"); ReadOutputs(); }
            });
        }
        // Two explicit ranges also leave Y17 untouched on older firmware.
        // Not atomic: failure can leave the first range changed. Never retry writes.
        private bool WriteControlledOutputs(bool[] values, bool demo = false)
        {
            _stop.Token.ThrowIfCancellationRequested();
            if (demo && !_demo) return false;
            if (!_client.WriteMultipleCoils(0, values, 0, IoModuleMap.CommunicationLedOutput)) return false;
            _stop.Token.ThrowIfCancellationRequested();
            if (demo && !_demo) return false;
            int start = IoModuleMap.CommunicationLedOutput + 1;
            return _client.WriteMultipleCoils((ushort)start, values, start, IoModuleMap.PhysicalOutputs - start);
        }
        private void SetOutputs(bool on)
        {
            bool[] values = new bool[IoModuleMap.PhysicalOutputs];
            for (int i = 0; i < values.Length; i++) values[i] = on;
            if (WriteControlledOutputs(values)) { Log(IoModuleMap.OutputScope + (on ? " 全部开" : " 全部关") + "；正在读回"); ReadOutputs(); }
            else Log("输出组写入失败，可能已部分执行；未自动重试，请核对实际输出。");
        }
        public void SetAllOutputs(bool on)
        {
            _demo = false;
            if (!on) { if (!_connected) throw new InvalidOperationException("串口未连接"); Interlocked.Exchange(ref _allOff, 1); }
            else Queue(() => SetOutputs(true));
        }
        public void WriteRegister(int address, ushort value)
        {
            if (address < 0 || address >= 19) throw new ArgumentOutOfRangeException(nameof(address));
            Queue(() =>
            {
                if (!_client.WriteSingleRegister((ushort)address, value)) return;
                Log("写保持寄存器 0x" + address.ToString("X4") + " = " + value + "；正在读回");
                ushort[] r = new ushort[19];
                if (_client.ReadHoldingRegisters(0, 19, r, 0)) lock (_gate) { Array.Copy(r, _regs, 19); _hasR = true; }
            });
        }
        public void SendRaw(byte[] request)
        {
            byte[] copy = (byte[])request.Clone();
            Queue(() => { _capture = true; try { _client.SendRaw(copy, out _); } finally { _capture = false; } });
        }
        public IoSnapshot Snapshot()
        {
            lock (_gate)
            {
                long now = _clock.ElapsedMilliseconds;
                return new IoSnapshot
                {
                    Connected = _connected, Polling = _polling, Demo = _demo,
                    X = (bool[])_x.Clone(), Y = (bool[])_y.Clone(), Analog = (ushort[])_analog.Clone(), Registers = (ushort[])_regs.Clone(), Diagnostics = (ushort[])_diag.Clone(),
                    HasInputs = _hasX, HasOutputs = _hasY, HasAnalog = _hasA, HasRegisters = _hasR, DiagnosticsSupported = _diagSupported,
                    Tx = _tx, Rx = _rx, Errors = _errors, PollCycles = _cycles, GoodCycles = _goodCycles,
                    LastResponseAgeMs = _lastResponse < 0 ? long.MaxValue : now - _lastResponse,
                    LastIoAgeMs = _lastIo < 0 ? long.MaxValue : now - _lastIo,
                    ElapsedMs = now, LastLatencyMs = _lastLatency, MaxLatencyMs = _maxLatency, LastError = _lastError
                };
            }
        }
    }
}
