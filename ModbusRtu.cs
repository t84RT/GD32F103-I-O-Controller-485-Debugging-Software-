using System;
using System.IO.Ports;

namespace IOModbusTester
{
    /// <summary>
    /// Modbus-RTU 通信类，与 IOCON 工程（msPLC-1000 IO 模块）协议完全匹配：
    /// 115200-8-N-1，CRC16 低字节在前，功能码 01/02/03/04/05/06/0F/10。
    /// 线程安全：所有收发在内部锁内串行执行。
    /// </summary>
    public class ModbusRtu : IModbusClient
    {
        private readonly SerialPort _port;
        private readonly object _sync = new object();
        private readonly byte _station;
        private readonly int _timeoutMs;
        private readonly System.Diagnostics.Stopwatch _responseTimer = new System.Diagnostics.Stopwatch();
        private bool _disposed;
        private readonly System.Diagnostics.Stopwatch _busClock = System.Diagnostics.Stopwatch.StartNew();
        private readonly int _gapMs;
        private long _lastTransactionEnd;

        /// <summary>发送一帧（HEX 文本）</summary>
        public event Action<string> FrameSent;
        /// <summary>接收一帧（HEX 文本）</summary>
        public event Action<string> FrameReceived;
        /// <summary>完整原始应答，在校验前提供给帧监视；不能据此判断在线。</summary>
        public event Action<string> FrameObserved;
        /// <summary>通信异常（文本）</summary>
        public event Action<string> CommError;

        public bool IsOpen => _port != null && _port.IsOpen;
        public string PortName => _port?.PortName;
        public byte Station => _station;
        public byte? LastExceptionCode { get; private set; }

        public ModbusRtu(string portName, byte station = IoModuleMap.DefaultStation, int baud = 115200,
                         int dataBits = 8, Parity parity = Parity.None,
                         StopBits stopBits = StopBits.One, int timeoutMs = 300)
        {
            if (station < 1 || station > 247) throw new ArgumentOutOfRangeException(nameof(station));
            if (timeoutMs < 1 || timeoutMs > 60000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            _station = station;
            _timeoutMs = timeoutMs;
            double characterBits = 1 + dataBits + (parity == Parity.None ? 0 : 1) + (stopBits == StopBits.Two ? 2 : stopBits == StopBits.OnePointFive ? 1.5 : 1);
            _gapMs = Math.Max(2, (int)Math.Ceiling(3500.0 * characterBits / baud));
            _lastTransactionEnd = -_gapMs;
            _port = new SerialPort(portName, baud, parity, dataBits, stopBits)
            {
                ReadTimeout = timeoutMs,
                WriteTimeout = timeoutMs,
                DtrEnable = true,        // 适配各类 USB-CDC：多数芯片需 DTR 就绪才回传数据
                RtsEnable = true,        // 适配靠 RTS 控制方向/流控的 USB转485 模块
                Handshake = Handshake.None,   // 二进制 Modbus 禁用 XON/XOFF，避免 0x11/0x13 被吞
                ReadBufferSize = 8192,
                WriteBufferSize = 8192
            };
        }

        public void Open()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ModbusRtu));
                if (_port.IsOpen) return;
                _port.Open();
                _port.DiscardInBuffer();
                System.Threading.Thread.Sleep(120);
            }
        }
        public void Close()
        {
            lock (_sync) { if (!_disposed && _port.IsOpen) _port.Close(); }
        }
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _port.Dispose();
                _disposed = true;
            }
        }

        // ==================== 基础：CRC / 组帧 / 事务 ====================

        public static ushort Crc16(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }

        public static string ToHex(byte[] data, int count)
        {
            var sb = new System.Text.StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>组一帧：站号 + 功能码 + 地址 + 数据区 + CRC</summary>
        private byte[] BuildFrame(byte func, ushort addr, ushort value, byte[] data)
        {
            int head = 6;
            int dataLen = data == null ? 0 : data.Length;
            // 0x0F 写多线圈 / 0x10 写多寄存器：标准帧在“数量”后、数据前必须有 1 字节 byteCount
            bool needByteCount = (func == 0x0F || func == 0x10) && dataLen > 0;
            int extra = needByteCount ? 1 : 0;
            int len = head + extra + dataLen;
            byte[] f = new byte[len + 2];
            f[0] = _station;
            f[1] = func;
            f[2] = (byte)(addr >> 8);
            f[3] = (byte)addr;
            f[4] = (byte)(value >> 8);
            f[5] = (byte)value;
            if (needByteCount) f[6] = (byte)dataLen;   // byteCount = 后续数据字节数
            if (dataLen > 0) Array.Copy(data, 0, f, 6 + extra, dataLen);
            ushort crc = Crc16(f, 0, len);
            f[len] = (byte)(crc & 0xFF);
            f[len + 1] = (byte)(crc >> 8);
            return f;
        }

        private void ReadBytes(byte[] buffer, int offset, int count)
        {
            int got = 0;
            while (got < count)
            {
                int remaining = _timeoutMs - (int)_responseTimer.ElapsedMilliseconds;
                if (remaining <= 0) throw new TimeoutException("完整应答超时");
                _port.ReadTimeout = remaining;
                int n = _port.Read(buffer, offset + got, count - got);
                if (n <= 0) throw new TimeoutException("串口读取超时");
                got += n;
            }
        }

        private byte ReadByte()
        {
            byte[] b = new byte[1];
            ReadBytes(b, 0, 1);
            return b[0];
        }

        /// <summary>根据功能码读取完整应答帧</summary>
        private byte[] ReadResponse()
        {
            byte[] head = new byte[2];
            ReadBytes(head, 0, 2);
            byte func = head[1];

            byte byteCount = 0;
            int off;        // full 中接下来从串口读取的起始下标
            int totalLen;   // 完整帧总长度
            if ((func & 0x80) != 0)
            {
                off = 2;
                totalLen = 2 + 1 + 2;                              // 头2 + 异常码1 + CRC2
            }
            else if (func == 0x01 || func == 0x02 || func == 0x03 || func == 0x04)
            {
                byteCount = ReadByte();                            // 第3字节，必须保留，不可丢弃
                off = 3;
                totalLen = 2 + 1 + byteCount + 2;                  // 头2 + byteCount1 + 数据 + CRC2
            }
            else if (func == 0x05 || func == 0x06 || func == 0x0F || func == 0x10)
            {
                off = 2;
                totalLen = 2 + 4 + 2;                              // 头2 + 回显4 + CRC2
            }
            else
                throw new InvalidOperationException("未知功能码应答: " + func.ToString("X2"));

            byte[] full = new byte[totalLen];
            full[0] = head[0];
            full[1] = head[1];
            if (off == 3) full[2] = byteCount;                     // 回填 byteCount
            ReadBytes(full, off, totalLen - off);
            return full;
        }

        /// <summary>发送请求并等待校验应答；成功返回应答数据区指针后的完整帧</summary>
        private byte[] Transact(byte[] frame)
        {
            lock (_sync)
            {
                LastExceptionCode = null;
                if (_disposed || !_port.IsOpen) throw new InvalidOperationException("串口未打开");
                int remainingGap = _gapMs - (int)Math.Min(int.MaxValue, _busClock.ElapsedMilliseconds - _lastTransactionEnd);
                if (remainingGap > 0) System.Threading.Thread.Sleep(remainingGap);
                try
                {
                    _port.DiscardInBuffer();
                    _port.Write(frame, 0, frame.Length);
                    _responseTimer.Restart();
                    FrameSent?.Invoke(ToHex(frame, frame.Length));
                    byte[] resp = ReadResponse();
                    FrameObserved?.Invoke(ToHex(resp, resp.Length));
                    ValidateResponse(_station, frame, resp);
                    FrameReceived?.Invoke(ToHex(resp, resp.Length));
                    return resp;
                }
                finally { _lastTransactionEnd = _busClock.ElapsedMilliseconds; }
            }
        }
        // Separate serial I/O from validation, so tests never touch real outputs.
        internal static void ValidateResponse(byte station, byte[] request, byte[] response)
        {
            if (request == null || request.Length < 4)
                throw new ArgumentException("请求帧过短");
            if (response == null || response.Length < 5)
                throw new Exception("应答帧过短");
            if (response[0] != station)
                throw new Exception("应答站号不匹配: " + response[0].ToString("X2"));
            ushort crcRx = (ushort)(response[response.Length - 2] | (response[response.Length - 1] << 8));
            if (crcRx != Crc16(response, 0, response.Length - 2))
                throw new Exception("应答 CRC 错误");
            if (response[1] != request[1] && response[1] != (request[1] | 0x80))
                throw new Exception("应答功能码与请求不匹配");
            if ((response[1] & 0x80) != 0)
            {
                if (response.Length != 5) throw new Exception("异常应答长度错误");
                throw new ModbusException(response[2]);
            }
            if (request.Length < 8) throw new ArgumentException("请求帧长度错误");
            int quantity = (request[4] << 8) | request[5];
            switch (request[1])
            {
                case 0x01:
                case 0x02:
                case 0x03:
                case 0x04:
                    int expected = request[1] <= 0x02 ? (quantity + 7) / 8 : quantity * 2;
                    if (quantity == 0 || expected > 250 || response[2] != expected || response.Length != expected + 5)
                        throw new Exception("读取应答字节数与请求数量不匹配");
                    break;
                case 0x05:
                case 0x06:
                case 0x0F:
                case 0x10:
                    if (response.Length != 8) throw new Exception("写入应答长度错误");
                    for (int i = 2; i < 6; i++)
                        if (response[i] != request[i])
                            throw new Exception("写入应答回显与请求地址或数据不匹配");
                    break;
                default:
                    throw new Exception("未知功能码应答");
            }
        }

        private void ReportError(Exception ex)
        {
            LastExceptionCode = ex is ModbusException modbus ? modbus.Code : (byte?)null;
            CommError?.Invoke(ex.Message);
        }

        private static void CheckRange(Array values, int offset, int count, int maximum, ushort address)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (count <= 0 || count > maximum || (uint)address + count > 65536)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset < 0 || offset > values.Length || count > values.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(offset));
        }
        // ==================== 功能码操作 ====================

        /// <summary>0x01 读线圈（Y 输出状态）</summary>
        public bool ReadCoils(ushort addr, ushort count, bool[] dest, int destOffset)
        {
            try
            {
                CheckRange(dest, destOffset, count, 2000, addr);
                byte[] r = Transact(BuildFrame(0x01, addr, count, null));
                if (r.Length != 3 + r[2] + 2) throw new Exception("线圈应答长度异常");
                for (int i = 0; i < count; i++)
                    dest[destOffset + i] = (r[3 + (i >> 3)] & (1 << (i & 7))) != 0;
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>0x02 读离散输入（X 输入状态）</summary>
        public bool ReadDiscreteInputs(ushort addr, ushort count, bool[] dest, int destOffset)
        {
            try
            {
                CheckRange(dest, destOffset, count, 2000, addr);
                byte[] r = Transact(BuildFrame(0x02, addr, count, null));
                if (r.Length != 3 + r[2] + 2) throw new Exception("输入应答长度异常");
                for (int i = 0; i < count; i++)
                    dest[destOffset + i] = (r[3 + (i >> 3)] & (1 << (i & 7))) != 0;
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>0x03 读保持寄存器（4x 寄存器表）</summary>
        public bool ReadHoldingRegisters(ushort addr, ushort count, ushort[] dest, int destOffset)
        {
            try
            {
                CheckRange(dest, destOffset, count, 125, addr);
                byte[] r = Transact(BuildFrame(0x03, addr, count, null));
                if (r.Length != 3 + count * 2 + 2) throw new Exception("寄存器应答长度异常");
                for (int i = 0; i < count; i++)
                    dest[destOffset + i] = (ushort)((r[3 + i * 2] << 8) | r[4 + i * 2]);
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>0x04 读输入寄存器（A0~A5 模拟量）</summary>
        public bool ReadInputRegisters(ushort addr, ushort count, ushort[] dest, int destOffset)
        {
            try
            {
                CheckRange(dest, destOffset, count, 125, addr);
                byte[] r = Transact(BuildFrame(0x04, addr, count, null));
                if (r.Length != 3 + count * 2 + 2) throw new Exception("模拟量应答长度异常");
                for (int i = 0; i < count; i++)
                    dest[destOffset + i] = (ushort)((r[3 + i * 2] << 8) | r[4 + i * 2]);
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>0x05 写单个线圈（Y 输出）</summary>
        public bool WriteSingleCoil(ushort addr, bool value)
        {
            try
            {
                byte[] r = Transact(BuildFrame(0x05, addr, value ? (ushort)0xFF00 : (ushort)0x0000, null));
                return r.Length == 8;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>0x0F 写多个线圈（Y 输出）</summary>
        public bool WriteMultipleCoils(ushort addr, bool[] values, int offset, int count)
        {
            try
            {
                CheckRange(values, offset, count, 1968, addr);
                int byteCount = (count + 7) / 8;
                byte[] data = new byte[byteCount];
                for (int i = 0; i < count; i++)
                    if (values[offset + i]) data[i >> 3] |= (byte)(1 << (i & 7));
                byte[] r = Transact(BuildFrame(0x0F, addr, (ushort)count, data));
                return r.Length == 8;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>0x06 写单个寄存器</summary>
        public bool WriteSingleRegister(ushort addr, ushort value)
        {
            try
            {
                byte[] r = Transact(BuildFrame(0x06, addr, value, null));
                return r.Length == 8;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>0x10 写多个寄存器</summary>
        public bool WriteMultipleRegisters(ushort addr, ushort[] values, int offset, int count)
        {
            try
            {
                CheckRange(values, offset, count, 123, addr);
                byte[] data = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    data[i * 2] = (byte)(values[offset + i] >> 8);
                    data[i * 2 + 1] = (byte)values[offset + i];
                }
                byte[] r = Transact(BuildFrame(0x10, addr, (ushort)count, data));
                return r.Length == 8;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>手动发送原始请求帧（不含 CRC，自动补 CRC），等待并校验应答。用于诊断/调试。</summary>
        public bool SendRaw(byte[] requestNoCrc, out byte[] response)
        {
            response = null;
            try
            {
                if (requestNoCrc == null || requestNoCrc.Length < 4 || requestNoCrc.Length > 254)
                    throw new ArgumentException("原始请求须为4～254字节，不含CRC");
                if (requestNoCrc[0] != _station) throw new ArgumentException("原始帧站号必须与所选站号一致");
                byte[] frame = new byte[requestNoCrc.Length + 2];
                Array.Copy(requestNoCrc, frame, requestNoCrc.Length);
                ushort crc = Crc16(frame, 0, requestNoCrc.Length);
                frame[requestNoCrc.Length] = (byte)(crc & 0xFF);
                frame[requestNoCrc.Length + 1] = (byte)(crc >> 8);
                response = Transact(frame);
                return true;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }
    }
}
