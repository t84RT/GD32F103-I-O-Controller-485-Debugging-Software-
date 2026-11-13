using System;
using System.Drawing;
using System.Windows.Forms;

namespace IOModbusTester
{
    /// <summary>
    /// 数字孪生面板：自绘 IO 模块正面。
    /// 左侧 X0~15 输入端子（绿灯，由轮询读回刷新），
    /// 右侧 Y0~31 输出端子（Y17蓝色通讯专用，其余青绿色灯可点击写线圈），
    /// 中间 A0~A5 模拟量，顶部状态灯，底部模块信息。
    /// </summary>
    public class IoModulePanel : Control
    {
        public readonly bool[] X = new bool[64];
        public readonly bool[] Y = new bool[64];
        public readonly ushort[] Adc = new ushort[6];
        public bool AnalogKnown;
        public bool PowerLed, RunLed, CommRxLed, CommTxLed, WdLed, AlarmLed;
        public string Firmware = "--";
        public string ModuleType = "--";
        public string CommMs = "--";
        public string CommState = "未连接";
        public string Communication = "MODBUS-RTU / RS485";

        public event Action<int> YClicked;

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _font.Dispose(); _fontSmall.Dispose(); _fontTitle.Dispose(); _tip.Dispose(); }
            base.Dispose(disposing);
        }
        private const int PanelMargin = 14;
        private Rectangle _titleRect, _ledRect, _xRect, _adcRect, _yRect, _footRect;
        private int _sectionTitleY, _sectionLineY;
        private readonly Rectangle[] _xCells = new Rectangle[16];
        private readonly Rectangle[] _yCells = new Rectangle[32];
        private readonly Font _font = new Font("Microsoft YaHei", 9F);
        private readonly Font _fontSmall = new Font("Microsoft YaHei", 8F);
        private readonly Font _fontTitle = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
        private readonly ToolTip _tip = new ToolTip { InitialDelay = 120, ReshowDelay = 60, AutomaticDelay = 300 };

        public IoModulePanel()
        {
            DoubleBuffered = true;
            BackColor = IndustrialTheme.Surface;
            MinimumSize = new Size(860, 400);
            _tip.SetToolTip(this, "");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recalc();
        }

        private int D(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void Recalc()
        {
            int w=ClientSize.Width, h=ClientSize.Height, pad=12;
            int titleH=Math.Max(28,_fontTitle.Height+8), ledH=Math.Max(22,_fontSmall.Height+6);
            _titleRect=new Rectangle(pad,pad,w-2*pad,titleH);
            _ledRect=new Rectangle(pad,_titleRect.Bottom+4,w-2*pad,ledH);
            _sectionTitleY=_ledRect.Bottom+6;
            _sectionLineY=_sectionTitleY+Math.Max(18,_fontSmall.Height+4);
            int bodyTop=_sectionLineY+7, footH=Math.Max(22,_fontSmall.Height+7);
            int bodyH=Math.Max(8*24,h-bodyTop-pad-footH-8);
            _xRect=new Rectangle(pad,bodyTop,Math.Min(D(252),(int)(w*.235)),bodyH);
            _adcRect=new Rectangle(_xRect.Right+10,bodyTop,Math.Min(D(205),(int)(w*.195)),bodyH);
            _yRect=new Rectangle(_adcRect.Right+10,bodyTop,w-pad-_adcRect.Right-10,bodyH);
            _footRect=new Rectangle(pad,bodyTop+bodyH+6,w-2*pad,footH);
            int cellH=(bodyH-6)/8, xW=(_xRect.Width-12)/2, yW=(_yRect.Width-12)/4;
            for(int r=0;r<8;r++)
            {
                for(int c=0;c<2;c++)_xCells[r*2+c]=new Rectangle(_xRect.X+6+c*xW,_xRect.Y+3+r*cellH,xW-5,cellH-4);
                for(int c=0;c<4;c++)_yCells[r*4+c]=new Rectangle(_yRect.X+6+c*yW,_yRect.Y+3+r*cellH,yW-5,cellH-4);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width, h = ClientSize.Height;

            g.Clear(IndustrialTheme.Surface);
            using (var edge = new Pen(IndustrialTheme.Border)) g.DrawRectangle(edge,0,0,w-1,h-1);
            using (var header = new SolidBrush(IndustrialTheme.Soft)) g.FillRectangle(header,1,1,w-2,_ledRect.Bottom+D(5));
            using (var stripe = new SolidBrush(IndustrialTheme.Accent)) g.FillRectangle(stripe,0,0,D(4),h);

            // 标题
            DrawTitle(g, _titleRect);

            // 状态灯
            DrawLedStrip(g, _ledRect);

            // X / Y / ADC 分区标题
            using (var section = new SolidBrush(IndustrialTheme.Muted))
            using (var line = new Pen(IndustrialTheme.Border))
            {
                g.DrawString("数字输入 / 16 DI · 只读", _fontSmall, section, _xRect.X + 2, _sectionTitleY);
                g.DrawString("模拟量  /  6 AI · RAW", _fontSmall, section, _adcRect.X + 2, _sectionTitleY);
                g.DrawString("数字输出 / 32 DO · Y17通讯专用 / 只读", _fontSmall, section, _yRect.X + 2, _sectionTitleY);
                g.DrawLine(line, _xRect.X, _sectionLineY, _xRect.Right, _sectionLineY);
                g.DrawLine(line, _adcRect.X, _sectionLineY, _adcRect.Right, _sectionLineY);
                g.DrawLine(line, _yRect.X, _sectionLineY, _yRect.Right, _sectionLineY);
            }

            // X 端子
            for (int i = 0; i < 16; i++)
                DrawTerminal(g, _xCells[i], "X" + i, X[i], IndustrialTheme.Good);

            // Y 端子
            for (int i = 0; i < 32; i++)
                DrawTerminal(g, _yCells[i], "Y" + i, Y[i], i == IoModuleMap.CommunicationLedOutput ? IndustrialTheme.Blue : IndustrialTheme.Accent);

            // ADC
            DrawAdc(g, _adcRect);

            // All values remain actual readback; this is not a simulated device preview.
            string footer = "协议 " + Firmware + "   |   " + ModuleType + "   |   最近应答 " + CommMs + " ms   |   " + CommState + "   |   Y17=485通讯灯；X16～63 / Y32～63 保留";
            TextRenderer.DrawText(g,footer,_fontSmall,_footRect,IndustrialTheme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }

        private void DrawTitle(Graphics g, Rectangle r)
        {
            IndustrialIcons.Draw(g,new Rectangle(r.X,r.Y+D(6),D(22),D(22)),Glyph.Module,IndustrialTheme.Accent);
            TextRenderer.DrawText(g,"现场 I/O 概览",_fontTitle,new Rectangle(r.X+D(32),r.Y,r.Width/2,r.Height),IndustrialTheme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g,Communication,_fontSmall,new Rectangle(r.X+r.Width/2,r.Y,r.Width/2,r.Height),IndustrialTheme.Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }

        private void DrawLedStrip(Graphics g, Rectangle r)
        {
            NextLedX = r.X;
            DrawLed(g, r, "ONLINE", PowerLed, IndustrialTheme.Good);
            DrawLed(g, r, "POLL", RunLed, IndustrialTheme.Accent);
            DrawLed(g, r, "RX", CommRxLed, IndustrialTheme.Warning);
            DrawLed(g, r, "TX", CommTxLed, IndustrialTheme.Blue);

            DrawLed(g, r, "ALARM", AlarmLed, IndustrialTheme.Danger);
        }

        private void DrawLed(Graphics g, Rectangle r, string label, bool on, Color color)
        {
            int x=NextLedX, s=D(9), y=r.Y+(r.Height-s)/2;
            using(var brush=new SolidBrush(on?color:IndustrialTheme.Border))g.FillEllipse(brush,x,y,s,s);
            TextRenderer.DrawText(g,label,_fontSmall,new Point(x+s+D(6),r.Y+D(3)),on?IndustrialTheme.Ink:IndustrialTheme.Muted);
            NextLedX=x+s+D(6)+TextRenderer.MeasureText(label,_fontSmall).Width+D(22);
        }

        private int NextLedX;

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < 32; i++)
                if (_yCells[i].Contains(e.Location))
                {
                    if (i == IoModuleMap.CommunicationLedOutput)
                        _tip.Show("Y17为485通讯专用灯，由固件自动闪烁，不能手动控制。", this, e.Location, 2500);
                    else YClicked?.Invoke(i);
                    return;
                }
            // X 是输入端子，点击不写入，仅提示
            for (int i = 0; i < 16; i++)
                if (_xCells[i].Contains(e.Location))
                {
                    _tip.Show("X" + i + " 是输入端子，由外部信号驱动（接24V/传感器），上位机只读，不可点击控制。", this, e.Location, 2500);
                    return;
                }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            for (int i = 0; i < 16; i++)
                if (_xCells[i].Contains(e.Location))
                {
                    _tip.SetToolTip(this, "X" + i + "：输入端子（只读），由外部信号驱动，接 24V 或传感器后此灯点亮");
                    return;
                }
            for (int i = 0; i < 32; i++)
                if (_yCells[i].Contains(e.Location))
                {
                    _tip.SetToolTip(this, i == IoModuleMap.CommunicationLedOutput ? "Y17：485通讯专用灯（只读），有效通讯时由新版固件闪烁" : "Y" + i + "：输出端子，点击立即写入线圈（0x05）");
                    return;
                }
            _tip.SetToolTip(this, "");
        }

        private void DrawTerminal(Graphics g, Rectangle cell, string name, bool on, Color onColor)
        {
            bool comm=name=="Y17";
            Color fill=comm?Color.FromArgb(235,244,250):on?Color.FromArgb(229,244,239):IndustrialTheme.Surface;
            using(var bg=new SolidBrush(fill))g.FillRectangle(bg,cell);
            using(var border=new Pen(comm?IndustrialTheme.Blue:on?onColor:IndustrialTheme.Border))g.DrawRectangle(border,cell.X,cell.Y,cell.Width-1,cell.Height-1);
            if(on)using(var stripe=new SolidBrush(onColor))g.FillRectangle(stripe,cell.X,cell.Y,D(3),cell.Height);
            int lampSize=Math.Min(D(11),cell.Height-D(8)), lx=cell.X+D(9), ly=cell.Y+(cell.Height-lampSize)/2;
            using(var lamp=new SolidBrush(on?onColor:IndustrialTheme.DisabledSurface))g.FillEllipse(lamp,lx,ly,lampSize,lampSize);
            using(var edge=new Pen(on?onColor:IndustrialTheme.DisabledInk))g.DrawEllipse(edge,lx,ly,lampSize,lampSize);
            int labelX=lx+lampSize+D(7), reserve=comm?D(30):D(14);
            TextRenderer.DrawText(g,name,_font,new Rectangle(labelX,cell.Y,Math.Max(1,cell.Right-labelX-reserve),cell.Height),comm?IndustrialTheme.Blue:IndustrialTheme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
            if(comm)TextRenderer.DrawText(g,"485",_fontSmall,new Rectangle(cell.Right-D(30),cell.Y,D(28),cell.Height),IndustrialTheme.Blue,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            else using(var pen=new Pen(IndustrialTheme.Border)) { int px=cell.Right-D(8), cy=cell.Y+cell.Height/2;g.DrawLine(pen,px-D(3),cy,px,cy); }
        }

        private void DrawAdc(Graphics g, Rectangle r)
        {
            int rowH=Math.Max(D(24),(r.Height-D(6))/6);
            for(int i=0;i<6;i++)
            {
                int y=r.Y+D(3)+i*rowH;
                TextRenderer.DrawText(g,"A"+i,_fontSmall,new Point(r.X+D(2),y+D(2)),IndustrialTheme.Ink);
                int barX=r.X+D(33), barW=r.Width-D(83);
                var barRect=new Rectangle(barX,y+D(8),barW,D(8));
                using(var bg=new SolidBrush(IndustrialTheme.DisabledSurface))g.FillRectangle(bg,barRect);
                int fill=AnalogKnown?(int)((barW-D(2))*Math.Min(Adc[i],(ushort)4095)/4095.0):0;
                if(fill>0)using(var bar=new SolidBrush(IndustrialTheme.Accent))g.FillRectangle(bar,barX+D(1),y+D(9),fill,D(6));
                TextRenderer.DrawText(g,AnalogKnown?Adc[i].ToString():"--",_fontSmall,new Rectangle(barX+barW+D(4),y,D(43),D(24)),IndustrialTheme.Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            }
        }
    }

    internal static class GraphicsEx
    {
        public static void FillRounded(this Graphics g, Brush b, int x, int y, int w, int h, int r)
        {
            using (var path = RoundedPath(x, y, w, h, r))
                g.FillPath(b, path);
        }

        public static void DrawRounded(this Graphics g, Pen p, int x, int y, int w, int h, int r)
        {
            using (var path = RoundedPath(x, y, w, h, r))
                g.DrawPath(p, path);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(int x, int y, int w, int h, int r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
