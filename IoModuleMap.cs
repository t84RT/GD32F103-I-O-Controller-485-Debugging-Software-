using System;
namespace IOModbusTester
{
    public sealed class ModbusException : Exception
    {
        public byte Code { get; }
        public ModbusException(byte code) : base("Modbus 异常码: " + code.ToString("X2")) { Code = code; }
    }
    public interface IModbusClient : IDisposable
    {
        bool IsOpen { get; }
        byte Station { get; }
        byte? LastExceptionCode { get; }
        string PortName { get; }
        event Action<string> FrameSent;
        event Action<string> FrameReceived;
        event Action<string> FrameObserved;
        event Action<string> CommError;
        void Open(); void Close();
        bool ReadCoils(ushort address, ushort count, bool[] values, int offset);
        bool ReadDiscreteInputs(ushort address, ushort count, bool[] values, int offset);
        bool ReadInputRegisters(ushort address, ushort count, ushort[] values, int offset);
        bool ReadHoldingRegisters(ushort address, ushort count, ushort[] values, int offset);
        bool WriteSingleCoil(ushort address, bool value);
        bool WriteMultipleCoils(ushort address, bool[] values, int offset, int count);
        bool WriteSingleRegister(ushort address, ushort value);
        bool WriteMultipleRegisters(ushort address, ushort[] values, int offset, int count);
        bool SendRaw(byte[] request, out byte[] response);
    }
    public static class IoModuleMap
    {
        public const int CommunicationLedOutput = 17;
        public const int ControllableOutputs = 31;
        public const string OutputScope = "Y0～16、Y18～31（31路，Y17为通讯灯）";
        public const byte DefaultStation = 3;
        public const int DefaultBaud = 115200;
        public const int DigitalCount = 64, PhysicalInputs = 16, PhysicalOutputs = 32, AnalogCount = 6;
        public const ushort DiagnosticAddress = 0x0100, DiagnosticCount = 16, DiagnosticSignature = 0x494F;
        public static readonly string[] RegisterNames =
        {
            "步进0当前位置·低16位", "步进0当前位置·高16位",
            "步进0目标Pstrt0·低16位", "步进0目标Pstrt0·高16位",
            "步进0目标Pstrt1·低16位", "步进0目标Pstrt1·高16位",
            "步进0目标Pstrt2·低16位", "步进0目标Pstrt2·高16位",
            "步进0目标Pstrt3·低16位", "步进0目标Pstrt3·高16位",
            "旋转速度Rotate.High·低16位", "旋转速度Rotate.High·高16位",
            "相机1触发延时ms", "工位1 NG处理延时ms", "工位1 OK/NG保持ms",
            "相机2触发延时ms", "工位2 NG处理延时ms", "工位2 OK/NG保持ms", "触发间隔"
        };
        public static readonly string[] DiagnosticNames =
        {
            "标识(IO)", "协议版本", "站号", "波特率·低16位", "波特率·高16位",
            "物理输入数", "物理输出数", "模拟量数", "运行秒数·低16位", "运行秒数·高16位",
            "格式有效请求·低16位", "格式有效请求·高16位", "CRC错误(饱和)", "接收溢出(饱和)", "格式错误(饱和)", "能力位(bit0:Y17通讯灯)"
        };
        public static byte[] ParseHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("请输入不含 CRC 的请求帧");
            var bytes = new System.Collections.Generic.List<byte>();
            foreach (string part in text.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part.Substring(2) : part;
                if (token.Length == 0 || (token.Length & 1) != 0) throw new FormatException("每个十六进制片段必须由完整字节组成");
                for (int i = 0; i < token.Length; i += 2) bytes.Add(Convert.ToByte(token.Substring(i, 2), 16));
            }
            if (bytes.Count < 4 || bytes.Count > 254) throw new FormatException("请求须为4～254字节，不含CRC");
            return bytes.ToArray();
        }
    }
}
