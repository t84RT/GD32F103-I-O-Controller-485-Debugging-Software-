using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace IOModbusTester
{
    // Original light industrial styling, inspired by engineering workbenches.
    // No Siemens branding, external theme package or transport dependency.
    internal static class IndustrialTheme
    {
        public static readonly Color Canvas = ColorTranslator.FromHtml("#EFF2F4");
        public static readonly Color Surface = Color.White;
        public static readonly Color Soft = ColorTranslator.FromHtml("#F5F7F8");
        public static readonly Color Ink = ColorTranslator.FromHtml("#172D3A");
        public static readonly Color Muted = ColorTranslator.FromHtml("#526674");
        public static readonly Color Border = ColorTranslator.FromHtml("#C7D2D8");
        public static readonly Color Accent = ColorTranslator.FromHtml("#007C80");
        public static readonly Color AccentSoft = ColorTranslator.FromHtml("#E1F2F1");
        public static readonly Color Danger = ColorTranslator.FromHtml("#B33136");
        public static readonly Color Warning = ColorTranslator.FromHtml("#956000");
        public static readonly Color Good = ColorTranslator.FromHtml("#237A4B");
        public static readonly Color Blue = ColorTranslator.FromHtml("#1969A0");
        public static readonly Color DisabledInk = ColorTranslator.FromHtml("#78858E");
        public static readonly Color DisabledSurface = ColorTranslator.FromHtml("#E9EDF0");

        public static Icon LoadIcon()
        {
            using (Stream stream = typeof(IndustrialTheme).Assembly.GetManifestResourceStream("IOModbusTester.Assets.iocon.ico"))
            {
                if (stream == null) throw new InvalidOperationException("Missing application icon resource");
                using (var icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
            }
        }
        public static void Apply(Control control)
        {
            control.ForeColor = control.ForeColor == Color.DarkRed ? Danger : Ink;
            if (control is IoModulePanel || control is WorkbenchHeader || control is IndustrialButton) return;
            control.BackColor = control is Form || control is TableLayoutPanel || control is SplitContainer ? Canvas : Surface;
            if (control is TextBoxBase text) { text.BackColor = Surface; text.ForeColor = Ink; text.BorderStyle = BorderStyle.FixedSingle; }
            if (control is ComboBox choice) { choice.FlatStyle = FlatStyle.Flat; choice.BackColor = Surface; }
            if (control is NumericUpDown number) { number.BorderStyle = BorderStyle.FixedSingle; number.BackColor = Surface; }
            if (control is DataGridView grid)
            {
                grid.EnableHeadersVisualStyles = false; grid.BackgroundColor = Surface;
                grid.BorderStyle = BorderStyle.None; grid.GridColor = Border;
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Soft;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Soft;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
                grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                grid.ColumnHeadersHeight = 32;
                grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Ink;
                grid.DefaultCellStyle.SelectionBackColor = AccentSoft; grid.DefaultCellStyle.SelectionForeColor = Ink;
                grid.DefaultCellStyle.Padding = new Padding(7, 2, 7, 2);
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 251);
                grid.RowTemplate.Height = 28;
            }
            if (control is StatusStrip strip) { strip.SizingGrip = true; strip.BackColor = Soft; strip.ForeColor = Muted; strip.RenderMode = ToolStripRenderMode.System; }
            foreach (Control child in control.Controls) Apply(child);
        }
        public static double Contrast(Color a, Color b)
        {
            Func<Color, double> luminance = c =>
            {
                Func<byte,double> linear = v => v/255.0 <= .04045 ? v/255.0/12.92 : Math.Pow((v/255.0+.055)/1.055,2.4);
                return .2126*linear(c.R)+.7152*linear(c.G)+.0722*linear(c.B);
            };
            double x=luminance(a), y=luminance(b); return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
        }
    }

    internal enum Glyph { Module, Refresh, Connect, Close, Play, Pause, On, Off, Demo, Stop, Write, Send, Export, Clear, Registers, Diagnostics, Monitor, Metrics, Help }
    internal static class IndustrialIcons
    {
        public static void Draw(Graphics g, Rectangle bounds, Glyph glyph, Color color)
        {
            var state=g.Save();
            try
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/24f,bounds.Height/24f);
                using(var p=new Pen(color,1.65f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                using(var b=new SolidBrush(color))
                {
                    switch(glyph)
                    {
                        case Glyph.Module:
                            g.DrawRectangle(p,5,3,14,18); g.DrawLine(p,9,3,9,21);
                            for(int i=0;i<3;i++){g.FillRectangle(b,12,6+i*5,3,2);g.DrawLine(p,2,6+i*6,5,6+i*6);g.DrawLine(p,19,6+i*6,22,6+i*6);} break;
                        case Glyph.Refresh:
                            g.DrawArc(p,5,5,14,14,45,275);g.DrawLines(p,new[]{new Point(19,4),new Point(19,9),new Point(14,9)});break;
                        case Glyph.Connect:
                            g.DrawLine(p,9,3,9,8);g.DrawLine(p,15,3,15,8);g.DrawRectangle(p,6,8,12,6);g.DrawLines(p,new[]{new Point(9,14),new Point(9,18),new Point(15,18),new Point(15,21)});break;
                        case Glyph.Close:
                            g.DrawLines(p,new[]{new Point(9,4),new Point(4,4),new Point(4,20),new Point(9,20)});g.DrawLine(p,10,12,21,12);g.DrawLines(p,new[]{new Point(17,8),new Point(21,12),new Point(17,16)});break;
                        case Glyph.Play:case Glyph.Demo:
                            g.DrawPolygon(p,new[]{new Point(8,4),new Point(19,12),new Point(8,20)});if(glyph==Glyph.Demo){g.DrawLine(p,3,7,3,17);}break;
                        case Glyph.Pause:g.FillRectangle(b,6,5,4,14);g.FillRectangle(b,14,5,4,14);break;
                        case Glyph.Stop:g.DrawRectangle(p,5,5,14,14);g.FillRectangle(b,9,9,6,6);break;
                        case Glyph.On:case Glyph.Off:
                            g.DrawArc(p,4,5,16,16,-45,270);g.DrawLine(p,12,2,12,12);if(glyph==Glyph.Off)g.DrawLine(p,3,3,21,21);break;
                        case Glyph.Write:
                            g.DrawRectangle(p,4,4,15,16);g.DrawLine(p,8,4,8,10);g.DrawLine(p,8,10,15,10);g.DrawRectangle(p,8,15,7,5);break;
                        case Glyph.Send:g.DrawPolygon(p,new[]{new Point(3,4),new Point(22,12),new Point(3,20),new Point(7,12)});g.DrawLine(p,7,12,16,12);break;
                        case Glyph.Export:
                            g.DrawLines(p,new[]{new Point(5,14),new Point(5,21),new Point(19,21),new Point(19,14)});g.DrawLine(p,12,3,12,16);g.DrawLines(p,new[]{new Point(8,12),new Point(12,16),new Point(16,12)});break;
                        case Glyph.Clear:g.DrawRectangle(p,7,7,10,14);g.DrawLine(p,4,7,20,7);g.DrawLine(p,9,3,15,3);g.DrawLine(p,10,11,10,17);g.DrawLine(p,14,11,14,17);break;
                        case Glyph.Registers:g.DrawRectangle(p,3,4,18,16);g.DrawLine(p,3,9,21,9);g.DrawLine(p,3,14,21,14);g.DrawLine(p,9,4,9,20);break;
                        case Glyph.Diagnostics:g.DrawCircle(p,12,12,9);g.DrawLines(p,new[]{new Point(5,12),new Point(9,12),new Point(11,7),new Point(14,17),new Point(16,12),new Point(20,12)});break;
                        case Glyph.Monitor:g.DrawRectangle(p,3,4,18,14);g.DrawLine(p,8,21,16,21);g.DrawLine(p,12,18,12,21);g.DrawLines(p,new[]{new Point(6,9),new Point(9,12),new Point(6,15)});g.DrawLine(p,12,14,17,14);break;
                        case Glyph.Metrics:g.DrawLine(p,4,20,21,20);g.DrawLine(p,4,3,4,20);g.DrawLines(p,new[]{new Point(7,16),new Point(11,10),new Point(15,13),new Point(20,5)});break;
                        case Glyph.Help:g.DrawEllipse(p,3,3,18,18);g.DrawArc(p,9,6,6,6,180,230);g.DrawLine(p,12,12,12,14);g.FillEllipse(b,11,17,2,2);break;
                    }
                }
            }
            finally {g.Restore(state);}
        }
        private static void DrawCircle(this Graphics g, Pen p, int x, int y, int radius) => g.DrawEllipse(p,x-radius,y-radius,radius*2,radius*2);
        public static Glyph ForButton(string text)
        {
            if(text.Contains("刷新"))return Glyph.Refresh;if(text.Contains("关闭串口"))return Glyph.Close;
            if(text.Contains("串口"))return Glyph.Connect;if(text.Contains("停止轮询"))return Glyph.Pause;
            if(text.Contains("轮询"))return Glyph.Play;if(text.Contains("全开"))return Glyph.On;
            if(text.Contains("全关"))return Glyph.Off;if(text.Contains("停止演示"))return Glyph.Stop;
            if(text.Contains("演示"))return Glyph.Demo;if(text.Contains("写入"))return Glyph.Write;
            if(text.Contains("发送"))return Glyph.Send;if(text.Contains("导出"))return Glyph.Export;
            if(text.Contains("清空"))return Glyph.Clear;return Glyph.Module;
        }
    }

    internal sealed class IndustrialButton : Button
    {
        private bool _hover, _pressed;
        public bool IconOnly { get; set; }
        public IndustrialButton()
        {
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
            Cursor=Cursors.Hand;Padding=new Padding(9,3,9,3);Margin=new Padding(3,2,3,2);
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            var text=TextRenderer.MeasureText(Text,Font);
            return new Size(Math.Max(MinimumSize.Width,IconOnly?Scale(30):text.Width+Scale(38)),Math.Max(MinimumSize.Height,Math.Max(Font.Height+12,Scale(26))));
        }
        private int Scale(int value)=>(int)Math.Round(value*DeviceDpi/96.0);
        protected override void OnMouseEnter(EventArgs e){_hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){_hover=false;_pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){_pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){_pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
        internal bool IsDanger => Text.Contains("全关")||Text.Contains("停止演示");
        internal bool IsPrimary => Text.Contains("打开串口")||Text.Contains("开始轮询")||Text.Contains("写入并读回");
        protected override void OnPaint(PaintEventArgs e)
        {
            Color role=IsDanger?IndustrialTheme.Danger:Text.Contains("全开")?IndustrialTheme.Warning:IndustrialTheme.Accent;
            bool fill=IsPrimary||IsDanger;
            Color background=!Enabled?IndustrialTheme.DisabledSurface:fill?role:_hover?IndustrialTheme.AccentSoft:IndustrialTheme.Surface;
            if(Enabled&&_pressed)background=ControlPaint.Dark(background,.07f);
            Color foreground=!Enabled?IndustrialTheme.DisabledInk:fill?Color.White:Text.Contains("全开")?IndustrialTheme.Warning:IndustrialTheme.Ink;
            using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,ClientRectangle);
            using(var pen=new Pen(!Enabled?IndustrialTheme.Border:fill?role:_hover?IndustrialTheme.Accent:IndustrialTheme.Border))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
            int s=Scale(17), left=IconOnly?(Width-s)/2:Scale(8);
            IndustrialIcons.Draw(e.Graphics,new Rectangle(left,(Height-s)/2,s,s),IndustrialIcons.ForButton(Text),foreground);
            if(!IconOnly) TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(left+s+Scale(6),0,Width-left-s-Scale(9),Height),foreground,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(3,3,Width-6,Height-6),foreground,background);
        }
    }

    internal sealed class IndustrialTabs : TabControl
    {
        private static readonly Glyph[] Icons={Glyph.Registers,Glyph.Diagnostics,Glyph.Monitor,Glyph.Metrics,Glyph.Help};
        public IndustrialTabs(){ShowToolTips=true;DrawMode=TabDrawMode.OwnerDrawFixed;SizeMode=TabSizeMode.Fixed;Padding=new Point(19,8);ItemSize=new Size(180,34);}
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Rectangle r=GetTabRect(e.Index); bool selected=e.Index==SelectedIndex;
            using(var bg=new SolidBrush(selected?IndustrialTheme.Surface:IndustrialTheme.Canvas))e.Graphics.FillRectangle(bg,r);
            Color ink=selected?IndustrialTheme.Accent:IndustrialTheme.Muted;
            int scale=Math.Max(1,DeviceDpi/96), iconSize=(int)(16*DeviceDpi/96f);
            IndustrialIcons.Draw(e.Graphics,new Rectangle(r.X+10*scale,r.Y+(r.Height-iconSize)/2,iconSize,iconSize),Icons[e.Index%Icons.Length],ink);
            TextRenderer.DrawText(e.Graphics,TabPages[e.Index].Text,Font,new Rectangle(r.X+34*scale,r.Y,r.Width-40*scale,r.Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            if(selected){using(var pen=new Pen(IndustrialTheme.Accent,3*scale))e.Graphics.DrawLine(pen,r.Left+1,r.Top+1,r.Right-1,r.Top+1);}
            if(Focused&&selected)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(r,-4,-4));
        }
    }

    internal sealed class WorkbenchHeader : Control
    {
        private readonly Font _title=new Font("Microsoft YaHei",14,FontStyle.Bold), _small=new Font("Microsoft YaHei",8.5f);
        public WorkbenchHeader(){DoubleBuffered=true;Dock=DockStyle.Fill;BackColor=IndustrialTheme.Surface;}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(IndustrialTheme.Surface);int s=(int)(DeviceDpi/96f*32), pad=(int)(DeviceDpi/96f*14);
            using(var b=new SolidBrush(IndustrialTheme.Accent))g.FillRectangle(b,pad,(Height-s)/2,s,s);
            IndustrialIcons.Draw(g,new Rectangle(pad+4,(Height-s)/2+4,s-8,s-8),Glyph.Module,Color.White);
            TextRenderer.DrawText(g,"IOCON  /  I/O 调试工作台",_title,new Rectangle(pad+s+12,2,Width/2,Height-4),IndustrialTheme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g,"GD32F103   ·   MODBUS RTU / RS485    |    V1.3.0",_small,new Rectangle(Width/2,0,Width/2-pad,Height),IndustrialTheme.Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            using(var p=new Pen(IndustrialTheme.Border))g.DrawLine(p,0,Height-1,Width,Height-1);
        }
        protected override void Dispose(bool disposing){if(disposing){_title.Dispose();_small.Dispose();}base.Dispose(disposing);}
    }
}
