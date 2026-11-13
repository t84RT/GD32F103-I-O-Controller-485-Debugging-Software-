using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IOModbusTester
{
    public sealed class MainForm : Form
    {
        private readonly ComboBox _ports = Choice(90), _baud = Choice(82), _parity = Choice(48), _dataBits = Choice(42), _stopBits = Choice(48);
        private readonly NumericUpDown _station = Number(1, 247, 3), _interval = Number(50, 5000, 100), _timeout = Number(100, 5000, 300);
        private readonly Button _connect = new IndustrialButton { Text = "打开串口", AutoSize = true };
        private readonly Button _poll = new IndustrialButton { Text = "开始轮询", AutoSize = true };
        private readonly Button _demo = new IndustrialButton { Text = "跑马灯演示", AutoSize = true };
        private readonly CheckBox _trace = new CheckBox { Text = "记录轮询帧（限量）", AutoSize = true };
        private readonly IoModulePanel _panel = new IoModulePanel { Dock = DockStyle.Fill };
        private readonly DataGridView _registers = Grid(), _diagnostics = Grid();
        private readonly ComboBox _regAddress = Choice(300);
        private readonly TextBox _regValue = new TextBox { Width = 100, Text = "0" };
        private readonly TextBox _raw = new TextBox { Width = 410, Text = "03 02 00 00 00 10" };
        private readonly RichTextBox _log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, BackColor = Color.FromArgb(28,32,42), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9F) };
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label _metrics = new Label { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei", 10F), Padding = new Padding(12), AutoSize = false };
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Icon _appIcon;
        private IoSession _session;
        private bool _connectionBusy, _closing, _allowClose;
        private long _lastTx, _lastRx;
        private string _portDescription = "未连接";

        public MainForm()
        {
            Text = "IOCON I/O 调试工作台 v1.3.0 · 工业浅色版";
            Font = new Font("Microsoft YaHei", 9F);
            _appIcon = IndustrialTheme.LoadIcon(); Icon = _appIcon;
            ClientSize = new Size(1280, 920); MinimumSize = SizeFromClientSize(new Size(1040, 760));
            StartPosition = FormStartPosition.CenterScreen;
            _baud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400" }); _baud.SelectedItem = "115200";
            _parity.Items.AddRange(new object[] { "无", "偶", "奇" }); _parity.SelectedIndex = 0;
            _dataBits.Items.AddRange(new object[] { "7", "8" }); _dataBits.SelectedIndex = 1;
            _stopBits.Items.AddRange(new object[] { "1", "1.5", "2" }); _stopBits.SelectedIndex = 0;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Controls.Add(layout);
            layout.Controls.Add(new WorkbenchHeader(), 0, 0);
            var toolbar = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=3, Margin=new Padding(0,4,0,4), Padding=new Padding(5,0,5,0) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute,40));toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute,38));toolbar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var top = new FlowLayoutPanel { Name="ConnectionToolbar", Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, Margin=Padding.Empty };
            var actions = new FlowLayoutPanel { Name="ActionToolbar", Dock=DockStyle.Fill, WrapContents=false, AutoScroll=true, Margin=Padding.Empty };
            AddLabel(top, "串口"); top.Controls.Add(_ports); var refresh = new IndustrialButton { Text="刷新", IconOnly=true, AutoSize=false, Size=new Size(36,32), AccessibleName="刷新串口列表" };
            refresh.Click += (s,e) => RefreshPorts(); top.Controls.Add(refresh); top.Controls.Add(_connect);
            AddLabel(top, "站号"); top.Controls.Add(_station); AddLabel(top, "波特率"); top.Controls.Add(_baud);
            AddLabel(top, "数据"); top.Controls.Add(_dataBits); AddLabel(top, "停止"); top.Controls.Add(_stopBits); AddLabel(top, "校验"); top.Controls.Add(_parity);
            var timeoutGroup = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            AddLabel(timeoutGroup, "超时ms"); timeoutGroup.Controls.Add(_timeout); top.Controls.Add(timeoutGroup);
            toolbar.Controls.Add(top,0,0);toolbar.Controls.Add(actions,0,1);
            AddLabel(actions, "轮询间隔ms"); actions.Controls.Add(_interval); actions.Controls.Add(_poll);
            actions.Controls.Add(Button("31路全开", () => Guard(() => { RequireSession(); if (Confirm("将打开Y0～16、Y18～31共31路输出；Y17保留为通讯灯。分两组写入，失败可能部分执行。请确认负载安全。")) _session.SetAllOutputs(true); })));
            actions.Controls.Add(Button("31路全关", () => Guard(() => { RequireSession(); _session.SetAllOutputs(false); })));
            actions.Controls.Add(_demo); actions.Controls.Add(_trace);
            toolbar.Controls.Add(new Label { Text="操作提示  /  Y17为通讯专用灯；全关不含Y17；停止演示或关闭连接不会自动清除输出，软件不能代替急停。", Dock=DockStyle.Fill, ForeColor=Color.DarkRed, TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true, Margin=new Padding(4,0,0,0), Font=new Font("Microsoft YaHei",8.25F) },0,2);
            layout.Controls.Add(toolbar,0,1);
            var split = new SplitContainer { Size = new Size(1240, 740), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 400, Panel1MinSize = 400, Panel2MinSize = 130 };
            split.Panel1.Controls.Add(_panel);
            var tabs = new IndustrialTabs { Dock = DockStyle.Fill };
            split.Panel2.Controls.Add(tabs); layout.Controls.Add(split, 0, 2);
            var regTab = new TabPage("保持寄存器"); tabs.TabPages.Add(regTab);
            regTab.Controls.Add(_registers);
            var writeBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38 };
            for (int i = 0; i < IoModuleMap.RegisterNames.Length; i++)
            {
                _registers.Rows.Add("0x" + i.ToString("X4"), IoModuleMap.RegisterNames[i], "--", "--");
                _regAddress.Items.Add("0x" + i.ToString("X4") + " " + IoModuleMap.RegisterNames[i]);
            }
            _regAddress.SelectedIndex = 0; writeBar.Controls.Add(_regAddress); writeBar.Controls.Add(_regValue);
            writeBar.Controls.Add(Button("写入并读回", WriteRegister));
            writeBar.Controls.Add(new Label { Text = "支持十进制/0x十六进制；0～11涉及步进控制", AutoSize = true, ForeColor = Color.DarkRed, Margin = new Padding(8,8,3,3) });
            regTab.Controls.Add(writeBar);
            var diagTab = new TabPage("板卡只读诊断") { ToolTipText="功能码04 / 地址0x0100～0x010F" }; tabs.TabPages.Add(diagTab); diagTab.Controls.Add(_diagnostics);
            for (int i = 0; i < 16; i++) _diagnostics.Rows.Add("0x" + (256+i).ToString("X4"), IoModuleMap.DiagnosticNames[i], "--", "--");
            var logTab = new TabPage("帧监视与日志"); tabs.TabPages.Add(logTab); logTab.Controls.Add(_log);
            var rawBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38 };
            AddLabel(rawBar, "原始请求（不含CRC）"); rawBar.Controls.Add(_raw); rawBar.Controls.Add(Button("发送", SendRaw));
            rawBar.Controls.Add(Button("导出日志", ExportLog)); rawBar.Controls.Add(Button("清空", () => _log.Clear())); logTab.Controls.Add(rawBar);
            var metricsTab = new TabPage("长稳统计"); tabs.TabPages.Add(metricsTab); metricsTab.Controls.Add(_metrics);
            var helpTab = new TabPage("说明与安全"); tabs.TabPages.Add(helpTab);
            helpTab.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Text = HelpText });
            var strip = new StatusStrip { Dock = DockStyle.Fill }; strip.Items.Add(_status); layout.Controls.Add(strip, 0, 3);
            _connect.Click += async (s,e) => await ToggleConnectionAsync();
            _poll.Click += (s,e) => Guard(() => { RequireSession(); _session.Polling = !_session.Polling; });
            _demo.Click += (s,e) => Guard(() => { RequireSession(); if (_session.Demo || Confirm("演示会循环改变Y0～16、Y18～31共31路输出，Y17保留为通讯灯。分两组写入，失败可能部分执行；停止后保持最后输出。确认负载安全后继续？")) _session.Demo = !_session.Demo; });
            _panel.YClicked += i => Guard(() => { RequireSession(); _session.ToggleOutput(i); });
            _interval.ValueChanged += (s,e) => { if (_session != null) _session.IntervalMs = (int)_interval.Value; };
            _trace.CheckedChanged += (s,e) => { if (_session != null) _session.TraceFrames = _trace.Checked; };
            FormClosing += ClosingAsync;
            _timer = new System.Windows.Forms.Timer { Interval = 100 }; _timer.Tick += (s,e) => RefreshView(); _timer.Start();
            IndustrialTheme.Apply(this);
            RefreshPorts(); RefreshView();
        }
        private static ComboBox Choice(int width) => new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
        private static NumericUpDown Number(int min,int max,int value) => new NumericUpDown { Minimum=min, Maximum=max, Value=value, Width=64 };
        private static Button Button(string text, Action action) { var b=new IndustrialButton { Text=text, AutoSize=true, Height=32 }; b.Click+=(s,e)=>action(); return b; }
        private static void AddLabel(FlowLayoutPanel p,string text) => p.Controls.Add(new Label { Text=text,AutoSize=true, Margin=new Padding(4,7,1,3), Font=new Font("Microsoft YaHei",8.25F) });
        private static DataGridView Grid()
        {
            var grid=new DataGridView { Dock=DockStyle.Fill, ReadOnly=true, AllowUserToAddRows=false, AllowUserToDeleteRows=false, RowHeadersVisible=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, SelectionMode=DataGridViewSelectionMode.FullRowSelect };
            grid.Columns.Add("address","协议地址");grid.Columns.Add("name","含义");grid.Columns.Add("decimal","十进制");grid.Columns.Add("hex","十六进制");grid.Columns[1].FillWeight=240;return grid;
        }
        private void Guard(Action action) { try { action(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning); } }
        private bool Confirm(string text) => MessageBox.Show(this,text,"输出/寄存器写入确认",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes;
        private void RequireSession() { if (_connectionBusy || _session==null || !_session.Snapshot().Connected) throw new InvalidOperationException("请先打开串口并等待连接完成"); }
        private void RefreshPorts()
        {
            Guard(() => { string current=_ports.Text; _ports.Items.Clear(); string[] ports=SerialPort.GetPortNames(); Array.Sort(ports,StringComparer.OrdinalIgnoreCase);_ports.Items.AddRange(ports);if(ports.Length>0){int index=_ports.Items.IndexOf(current);_ports.SelectedIndex=index>=0?index:0;} });
        }
        private async Task ToggleConnectionAsync()
        {
            if (_connectionBusy || _closing) return;
            _connectionBusy=true; _connect.Enabled=false;
            try
            {
                if (_session!=null)
                {
                    await _session.StopAsync(); DrainLogs(); _session=null;
                    _portDescription="未连接"; _lastTx=_lastRx=0;
                }
                else
                {
                    if (_ports.SelectedItem==null) throw new InvalidOperationException("没有可用串口");
                    var parity=_parity.SelectedIndex==1?Parity.Even:_parity.SelectedIndex==2?Parity.Odd:Parity.None;
                    var stop=_stopBits.SelectedIndex==1?StopBits.OnePointFive:_stopBits.SelectedIndex==2?StopBits.Two:StopBits.One;
                    string port=_ports.Text;
                    var client=new ModbusRtu(port,(byte)_station.Value,int.Parse(_baud.Text),int.Parse(_dataBits.Text),parity,stop,(int)_timeout.Value);
                    var next=new IoSession(client) { IntervalMs=(int)_interval.Value, TraceFrames=_trace.Checked };
                    _session=next;
                    try { await next.StartAsync(); }
                    catch { await next.StopAsync();DrainLogs();_session=null;throw; }
                    _portDescription=port+" / 站号"+_station.Value+" / "+_baud.Text+"-"+_dataBits.Text+"-"+_parity.Text+"-"+_stopBits.Text;
                    _session.Polling=true;
                }
            }
            catch(Exception ex) { MessageBox.Show(this,ex.Message,"串口连接",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
            finally { _connectionBusy=false;_connect.Enabled=true;RefreshView(); }
        }
        private void WriteRegister()
        {
            Guard(() =>
            {
                RequireSession();string text=_regValue.Text.Trim();ushort value=text.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?ushort.Parse(text.Substring(2),NumberStyles.HexNumber):ushort.Parse(text,CultureInfo.InvariantCulture);
                int index=_regAddress.SelectedIndex;
                if(Confirm("写入 "+_regAddress.Text+" = "+value+"？\n此处是原有业务寄存器，不是看门狗参数。"))_session.WriteRegister(index,value);
            });
        }
        private void SendRaw()
        {
            Guard(() =>
            {
                RequireSession();byte[] request=IoModuleMap.ParseHex(_raw.Text);
                if(request[0]!=(byte)_station.Value)throw new FormatException("帧站号必须与当前所选站号一致");
                bool read=request[1]>=1 && request[1]<=4;
                if(read || Confirm("原始请求可能写输出或业务寄存器，确认发送？"))_session.SendRaw(request);
            });
        }
        private void ExportLog()
        {
            using(var dialog=new SaveFileDialog { Filter="日志文本|*.txt",FileName="IO调试-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt" })
                if(dialog.ShowDialog(this)==DialogResult.OK)Guard(()=>File.WriteAllText(dialog.FileName,_status.Text+Environment.NewLine+_metrics.Text+Environment.NewLine+_log.Text,Encoding.UTF8));
        }
        private void DrainLogs()
        {
            if(_session==null)return;
            for(int i=0;i<50 && _session.TryTakeLog(out string line);i++)_log.AppendText(line+Environment.NewLine);
            if(_log.TextLength>180000){_log.Select(0,_log.TextLength-120000);_log.SelectedText="";}
        }
        private void RefreshView()
        {
            if(IsDisposed)return;
            IoSnapshot state=_session?.Snapshot();bool connected=state?.Connected==true;
            long threshold=Math.Max(2000,(long)_interval.Value*3);
            bool online=connected && state.LastResponseAgeMs<=threshold;
            bool fresh=online && state.LastIoAgeMs<=threshold;
            _connect.Text=_session==null?"打开串口":"关闭串口";
            foreach(Control control in new Control[]{_ports,_station,_baud,_parity,_dataBits,_stopBits,_timeout})control.Enabled=_session==null && !_connectionBusy;
            _poll.Enabled=connected&&!_connectionBusy;_poll.Text=state?.Polling==true?"停止轮询":"开始轮询";
            _demo.Enabled=connected&&!_connectionBusy;_demo.Text=state?.Demo==true?"停止演示":"跑马灯演示";
            _panel.Enabled=fresh && state?.Demo!=true;
            _panel.PowerLed=online;_panel.RunLed=state?.Polling==true;_panel.AlarmLed=connected&&!fresh;_panel.WdLed=false;
            _panel.CommTxLed=state!=null && state.Tx!=_lastTx;_panel.CommRxLed=state!=null && state.Rx!=_lastRx;
            _panel.CommState=!connected?"未连接":fresh?"IO数据有效":"等待/数据过期";
            _panel.CommMs=state==null || state.LastResponseAgeMs==long.MaxValue?"--":state.LastResponseAgeMs.ToString();
            _panel.AnalogKnown=state?.HasAnalog==true;
            _panel.Firmware=state?.DiagnosticsSupported==true?((state.Diagnostics[1]>>8)+"."+(state.Diagnostics[1]&255).ToString("D2")):"未报告(兼容模式)";
            _panel.ModuleType=state?.DiagnosticsSupported==true?"GD32F103 IO":"兼容IO板";
            _panel.Communication=_portDescription;
            if(state!=null)
            {
                if(state.HasInputs)Array.Copy(state.X,_panel.X,64);if(state.HasOutputs)Array.Copy(state.Y,_panel.Y,64);if(state.HasAnalog)Array.Copy(state.Analog,_panel.Adc,6);
                for(int i=0;i<19;i++)SetRow(_registers,i,state.HasRegisters?state.Registers[i]:(ushort?)null);
                for(int i=0;i<16;i++)SetRow(_diagnostics,i,state.DiagnosticsSupported==true?state.Diagnostics[i]:(ushort?)null);
                _lastTx=state.Tx;_lastRx=state.Rx;
                _status.Text=_portDescription+" | "+_panel.CommState+" | TX "+state.Tx+" / 有效RX "+state.Rx+" / ERR "+state.Errors+" | "+state.LastError;
                _metrics.Text="本次会话运行: "+TimeSpan.FromMilliseconds(state.ElapsedMs).ToString(@"dd\.hh\:mm\:ss")+Environment.NewLine+
                    "请求帧: "+state.Tx+"    有效应答: "+state.Rx+"    错误: "+state.Errors+Environment.NewLine+
                    "完整轮询: "+state.PollCycles+"    全部成功: "+state.GoodCycles+"    未全成功: "+(state.PollCycles-state.GoodCycles)+Environment.NewLine+
                    "最近应答耗时: "+state.LastLatencyMs.ToString("F1")+" ms    最大: "+state.MaxLatencyMs.ToString("F1")+" ms"+Environment.NewLine+
                    "板卡诊断: "+(state.DiagnosticsSupported==true?"已识别只读诊断区":state.DiagnosticsSupported==false?"旧固件/未识别，保留兼容读取":"等待探测")+Environment.NewLine+
                    "最近错误: "+state.LastError+Environment.NewLine+Environment.NewLine+
                    "TX按实际发送计数；RX只统计通过站号、CRC、功能码、长度及写回显校验的应答。\r\n停止轮询不会关闭串口；关闭连接/停止演示不自动改写输出。\r\n界面通信状态不能代替硬件电源指示，软件全部关不能代替急停。";
            }
            else
            {
                _status.Text="未连接；默认站号3、115200-8-N-1；先核对负载安全再操作输出。";
                _metrics.Text="打开串口并开始轮询后显示本次会话统计。";
                Array.Clear(_panel.X,0,64);Array.Clear(_panel.Y,0,64);Array.Clear(_panel.Adc,0,6);
                for(int i=0;i<19;i++)SetRow(_registers,i,null);for(int i=0;i<16;i++)SetRow(_diagnostics,i,null);
            }
            _panel.Invalidate();DrainLogs();
        }
        private static void SetRow(DataGridView grid,int index,ushort? value)
        {
            string dec=value?.ToString()??"--",hex=value.HasValue?"0x"+value.Value.ToString("X4"):"--";
            if(!Equals(grid.Rows[index].Cells[2].Value,dec))grid.Rows[index].Cells[2].Value=dec;
            if(!Equals(grid.Rows[index].Cells[3].Value,hex))grid.Rows[index].Cells[3].Value=hex;
        }
        private async void ClosingAsync(object sender,FormClosingEventArgs e)
        {
            if(_allowClose)return;
            e.Cancel=true;if(_closing)return;_closing=true;Enabled=false;
            await Task.Yield();
            // Connection startup can be in flight; let its await continuation finish first.
            while(_connectionBusy)await Task.Delay(25);
            try { if(_session!=null)await _session.StopAsync(); }
            finally { _timer.Stop();_timer.Dispose();_allowClose=true;Close(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer?.Dispose(); _appIcon?.Dispose(); }
            base.Dispose(disposing);
        }
        private const string HelpText = @"GD32F103 485 IO 控制板配套工具
默认站号3、115200-8-N-1；需与固件一致。串口打开后开始只读轮询，不自动写输出。

物理范围：X0～15输入、Y0～31输出、A0～A5模拟量；X16～63/Y32～63是保留位。
Y17为485通讯专用灯，蓝色显示、只读，不参与手动/全开/全关/跑马灯；新版固件自动闪烁。
跑马灯以3个相邻通道点亮的方式循环遍历Y0～16、Y18～31，约150ms一步（另加通信耗时）。
01/05/0F用于Y，02用于X，04地址0～5用于ADC原始值，03/06/10用于19个兼容保持寄存器。
保持寄存器0～11是步进位置/目标/速度，12～18是旧业务延时和间隔。这里不存在4～6地址的看门狗配置。
新固件另提供04功能码的0x0100～0x010F只读诊断区，不占用原有地址；旧固件仍可读写原接口。

点击Y先读取当前状态，再翻转并读回；全部开、演示和寄存器写入有确认提示。
写入失败不把界面灯强行改为成功。数据过期时禁用面板点击，保留的旧数据不能视为实时值。
全开/全关/演示分两组写入，不是原子操作，失败可能已部分执行；不自动重试。
全部关仅关31路可控输出，不关通讯灯；优先处理，并停止演示、清除待执行队列；它仍依赖电脑和485链路，不是硬件急停。
停止演示、停止轮询、关闭连接不自动改写输出。通信中断后的安全输出策略需按设备工艺另行确认。

原始帧不包含CRC；支持空格、逗号、分号、连续HEX及0x字节，拒绝杂字符，不会悄悄过滤输入。
新固件的诊断计数可配合长稳统计定位CRC、溢出或报文格式错误。日志为有界缓冲，长测可定期导出。
连接由单一后台工作线程管理；关闭后等待旧工作线程退出，再允许新会话。

请先断开危险负载，逐点验证，再进行拔插、上下电、负载干扰及至少24小时的实际运行验收。";
    }
}
