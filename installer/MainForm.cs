// 김쥬크 설치기 - 주크박스 모양 창 (레이어드 윈도우, 모든 그림을 직접 그림)
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        // exe 안에 넣은 Mono.Cecil.dll을 꺼내 쓰도록
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new System.Reflection.AssemblyName(e.Name).Name;
            using (var st = typeof(Program).Assembly.GetManifestResourceStream(name + ".dll"))
            {
                if (st == null) return null;
                var b = new byte[st.Length]; st.Read(b, 0, b.Length);
                return System.Reflection.Assembly.Load(b);
            }
        };
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    // ---- 게임별로 바꿀 부분 ------------------------------------------------
    const string GameTitle = "OXENFREE";
    const string PatchTitle = "한글패치 v" + Patcher.Version;
    const string SteamAppId = "388880";
    static readonly string[] BtnLabel = { "설치", "복원", "폴더", "실행", "닫기" };
    static readonly string[] BtnHint = {
        "게임에 한글패치를 적용합니다",
        "패치 전 원래 상태로 되돌립니다",
        "게임 폴더를 직접 고릅니다",
        "스팀으로 게임을 실행합니다",
        "설치기를 닫습니다",
    };
    // ------------------------------------------------------------------------

    Bitmap skin;
    float scale = 1f;
    int SkinW, SkinH;
    int hover = -1, pressed = -1;
    bool busy;
    string gameDir;
    string statusText = ""; Color statusColor = Color.White;
    readonly List<string> log = new List<string>();
    readonly Color[] ledColor = new Color[5];
    readonly DateTime[] ledUntil = new DateTime[5];
    System.Windows.Forms.Timer timer, anim;
    int blink;
    FontFamily ff;
    Bgm bgm;
    bool musicOn = true;
    static readonly Rectangle SpeakerRect = new Rectangle(Skin.Screen.Right - 30, Skin.Screen.Y + 6, 24, 22);
    const int SpeakerIdx = 5;

    static readonly Color Neon = Color.FromArgb(255, 172, 72);
    static readonly Color Cream = Color.FromArgb(250, 232, 200);
    static readonly Color Dim = Color.FromArgb(170, 140, 110);
    static readonly Color Ok = Color.FromArgb(120, 230, 120);
    static readonly Color Bad = Color.FromArgb(255, 95, 80);
    static readonly Color Warn = Color.FromArgb(255, 200, 90);

    public MainForm()
    {
        Text = "Oxenfree 한글패치";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        using (var st = typeof(MainForm).Assembly.GetManifestResourceStream("skin.png"))
            skin = new Bitmap(st);
        var wa = Screen.PrimaryScreen.WorkingArea;
        scale = Math.Min(1.25f, Math.Max(0.5f, wa.Height * 0.92f / Skin.Height));
        if (scale > 0.97f && scale < 1.03f) scale = 1f;
        SkinW = (int)Math.Round(Skin.Width * scale); SkinH = (int)Math.Round(Skin.Height * scale);
        Size = new Size(SkinW, SkinH);
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

        ff = PickFont("Malgun Gothic", "맑은 고딕", "Segoe UI");

        Patcher.Log = s => AddLog(s);
        timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (s, e) =>
        {
            blink++;
            if (bgm != null && musicOn) bgm.KeepLooping();
            bool ledActive = ledUntil.Any(u => u != DateTime.MinValue && u > DateTime.Now && u != DateTime.MaxValue) || ledUntil.Any(u => u == DateTime.MaxValue);
            if (busy || ledActive || ledExpired()) Render();
        };
        timer.Start();
        anim = new System.Windows.Forms.Timer { Interval = 33 };
        anim.Tick += (s, e) => { if (bgm != null && musicOn) Compose(); };
        anim.Start();

        musicOn = LoadSetting("Music", 1) != 0;
        bgm = Bgm.TryOpen(AppDomain.CurrentDomain.BaseDirectory);
        if (bgm != null && musicOn) bgm.Play();

        gameDir = Patcher.FindGameDir(null);
        RefreshStatus();
        ShowHelp();
    }

    static FontFamily PickFont(params string[] names)
    {
        var all = FontFamily.Families;
        foreach (var n in names)
        {
            var f = all.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            if (f != null) return f;
        }
        return FontFamily.GenericSansSerif;
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000; /* WS_EX_LAYERED */ return cp; }
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); Activate(); }

    // ------------------------------------------------------------------ 상태/기록
    void ShowHelp()
    {
        log.Clear();
        if (gameDir == null)
        {
            log.Add("게임 폴더를 찾지 못했습니다.");
            log.Add("[폴더] 버튼으로 OXENFREE.exe가");
            log.Add("있는 폴더를 골라 주세요.");
        }
        else if (Patcher.NeedsUpdate(gameDir))
        {
            log.Add("새 기능이 있습니다:");
            log.Add("자막 검은 바탕을 없애고 글씨만 표시");
            log.Add("");
            log.Add("[설치]를 다시 누르면 적용됩니다.");
        }
        else if (Patcher.IsPatched(gameDir))
        {
            log.Add("한글패치가 이미 적용되어 있습니다.");
            log.Add("[실행]을 눌러 게임을 시작하세요.");
            log.Add("");
            log.Add("스팀 업데이트로 영어로 돌아가면");
            log.Add("[설치]를 다시 누르면 됩니다.");
        }
        else
        {
            log.Add("[설치]를 누르면 게임에 들어 있는");
            log.Add("공식 한국어가 켜집니다.");
            log.Add("");
            log.Add("원본은 자동으로 백업되며");
            log.Add("[복원]으로 언제든 되돌릴 수 있습니다.");
        }
    }

    void RefreshStatus()
    {
        if (gameDir == null) { statusText = "✕  게임을 찾지 못함"; statusColor = Bad; }
        else if (Patcher.NeedsUpdate(gameDir)) { statusText = "◐  업데이트 있음"; statusColor = Warn; }
        else if (Patcher.IsPatched(gameDir)) { statusText = "●  한글패치 적용됨"; statusColor = Ok; }
        else { statusText = "○  아직 적용 안 됨"; statusColor = Warn; }
    }

    void AddLog(string s)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(AddLog), s); return; }
        log.Add(s);
        while (log.Count > 40) log.RemoveAt(0);
        Render();
    }

    void ToggleMusic()
    {
        if (bgm == null) return;
        musicOn = !musicOn;
        if (musicOn) bgm.Play(); else bgm.Pause();
        dirty = true;
        SaveSetting("Music", musicOn ? 1 : 0);
        Render();
    }

    const string RegKey = @"Software\KimJuke\Installer";
    static int LoadSetting(string name, int def)
    {
        try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKey)) return k == null ? def : Convert.ToInt32(k.GetValue(name, def)); }
        catch { return def; }
    }
    static void SaveSetting(string name, int v)
    {
        try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegKey)) k.SetValue(name, v, Microsoft.Win32.RegistryValueKind.DWord); }
        catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (bgm != null) bgm.Dispose();
        base.OnFormClosed(e);
    }

    bool[] ledWasOn = new bool[5];
    bool ledExpired()
    {
        bool changed = false; var now = DateTime.Now;
        for (int i = 0; i < 5; i++) { bool on = ledUntil[i] > now; if (on != ledWasOn[i]) { ledWasOn[i] = on; changed = true; } }
        return changed;
    }

    void SetLed(int i, Color c, double sec)
    {
        ledColor[i] = c; ledUntil[i] = sec > 0 ? DateTime.Now.AddSeconds(sec) : DateTime.MaxValue;
    }

    // ------------------------------------------------------------------ 버튼 동작
    void DoAction(int i)
    {
        if (busy && i != 4) return;
        switch (i)
        {
            case 0: RunJob(0, "설치", () => Patcher.Install(gameDir)); break;
            case 1:
                if (gameDir != null && Patcher.IsPatched(gameDir) &&
                    MessageBox.Show(this, "한글패치를 지우고 원래대로 복원할까요?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                RunJob(1, "복원", () => Patcher.Restore(gameDir));
                break;
            case 2: PickFolder(); break;
            case 3:
                try { System.Diagnostics.Process.Start("steam://rungameid/" + SteamAppId); SetLed(3, Ok, 2); AddLog("스팀으로 게임을 실행합니다..."); }
                catch (Exception ex) { SetLed(3, Bad, 3); AddLog("! 실행 실패: " + ex.Message); }
                break;
            case 4: Close(); break;
        }
        Render();
    }

    void RunJob(int btn, string name, Func<bool> job)
    {
        if (gameDir == null) { SetLed(btn, Bad, 3); AddLog("! 먼저 [폴더]로 게임 폴더를 골라 주세요."); return; }
        busy = true; log.Clear(); AddLog("── " + name + " 시작 ──");
        SetLed(btn, Warn, 0);
        var th = new Thread(() =>
        {
            bool ok = false;
            try { ok = job(); }
            catch (Exception ex) { Patcher.Log("! 오류: " + ex.Message); }
            BeginInvoke(new Action(() =>
            {
                busy = false;
                RefreshStatus();
                SetLed(btn, ok ? Ok : Bad, 6);
                if (ok) AddLog(btn == 0 ? "설치 완료!  [실행]으로 게임을 시작하세요." : "복원 완료!");
                else AddLog(name + " 실패. 위 내용을 확인해 주세요.");
            }));
        }) { IsBackground = true };
        th.Start();
    }

    void PickFolder()
    {
        using (var d = new FolderBrowserDialog())
        {
            d.Description = "OXENFREE.exe가 있는 게임 폴더를 골라 주세요.";
            if (gameDir != null) d.SelectedPath = gameDir;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var g = Patcher.FindGameDir(d.SelectedPath);
            if (g == null || !Patcher.IsGameDir(g) ||
                !(d.SelectedPath.StartsWith(g, StringComparison.OrdinalIgnoreCase) || g.StartsWith(d.SelectedPath, StringComparison.OrdinalIgnoreCase)))
            {
                SetLed(2, Bad, 3); AddLog("! 그 폴더에서 게임을 찾지 못했습니다.");
                return;
            }
            gameDir = g; SetLed(2, Ok, 3);
            RefreshStatus(); ShowHelp();
        }
    }

    // ------------------------------------------------------------------ 마우스/키보드
    int HitButton(Point p)
    {
        float x = p.X / scale, y = p.Y / scale;
        for (int i = 0; i < Skin.Buttons.Length; i++)
            if (Skin.Buttons[i].Contains((int)x, (int)y)) return i;
        if (bgm != null) { var r = SpeakerRect; r.Inflate(4, 4); if (r.Contains((int)x, (int)y)) return SpeakerIdx; }
        return -1;
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitButton(e.Location);
        if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Render(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; pressed = -1; Render(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int h = HitButton(e.Location);
        if (h >= 0) { pressed = h; Render(); return; }
        ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // 창 끌어서 옮기기
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        int p = pressed; pressed = -1;
        if (p == SpeakerIdx && HitButton(e.Location) == p) ToggleMusic();
        else if (p >= 0 && HitButton(e.Location) == p) DoAction(p);
        Render();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        else if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5) DoAction(e.KeyCode - Keys.D1);
        else if (e.KeyCode == Keys.M) ToggleMusic();
    }

    // ------------------------------------------------------------------ 그리기
    Font F(float px, FontStyle st = FontStyle.Regular) { return new Font(ff, px, st, GraphicsUnit.Pixel); }

    Bitmap cache; bool dirty = true;

    // 상태가 바뀔 때: 정적 그림(스킨+화면+버튼)을 다시 만들고 표시
    void Render() { dirty = true; Compose(); }

    void Compose()
    {
        if (!IsHandleCreated) return;
        int w = SkinW, h = SkinH;
        if (dirty || cache == null)
        {
            if (cache == null) cache = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cache))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawImage(skin, new Rectangle(0, 0, w, h));
                g.ScaleTransform(scale, scale);
                DrawScreen(g);
                DrawButtons(g);
            }
            dirty = false;
        }
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImageUnscaled(cache, 0, 0);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(scale, scale);
                DrawDancingNotes(g);
            }
            PushToWindow(bmp);
        }
    }

    // ---- 간판 '김쥬크' 양옆 음표: 음악에 맞춰 흔들림
    static readonly PointF NoteL = new PointF(195, 120), NoteR = new PointF(358, 120);
    const float NoteH = 30f;
    static readonly Color NoteCore = Color.FromArgb(255, 190, 168);
    static readonly Color NoteGlow = Color.FromArgb(255, 40, 40);

    void DrawDancingNotes(Graphics g)
    {
        float ang = 0, pop = 0;
        if (bgm != null && musicOn)
        {
            double ms = bgm.PositionMs();
            double beat = 60000.0 / bgm.Bpm;
            ang = (float)(Math.Sin(Math.PI * (ms - bgm.OffsetMs) / beat) * 11);   // 두 박자에 한 번 좌우로
            pop = bgm.EnergyAt(ms);                               // 드럼 칠 때 튀어 오름
        }
        DrawNote(g, NoteL, ang, pop, false);
        DrawNote(g, NoteR, -ang, pop, false);
    }

    static GraphicsPath notePath;
    static GraphicsPath NotePath()
    {
        if (notePath != null) return notePath;
        // 단위 크기(높이 1)의 이어진 8분음표 ♫ : 머리 2개, 기둥 2개, 굵은 꼬리 띠
        var p = new GraphicsPath();
        using (var m = new Matrix())
        {
            var h1 = new GraphicsPath(); h1.AddEllipse(-0.17f, -0.105f, 0.34f, 0.21f);
            m.Reset(); m.Translate(0.17f, 0.88f); m.Rotate(-22); h1.Transform(m); p.AddPath(h1, false);
            var h2 = new GraphicsPath(); h2.AddEllipse(-0.17f, -0.105f, 0.34f, 0.21f);
            m.Reset(); m.Translate(0.72f, 0.80f); m.Rotate(-22); h2.Transform(m); p.AddPath(h2, false);
        }
        p.AddPolygon(new[] { new PointF(0.29f, 0.84f), new PointF(0.355f, 0.84f), new PointF(0.39f, 0.10f), new PointF(0.325f, 0.11f) });
        p.AddPolygon(new[] { new PointF(0.84f, 0.76f), new PointF(0.905f, 0.76f), new PointF(0.94f, 0.02f), new PointF(0.875f, 0.03f) });
        p.AddPolygon(new[] { new PointF(0.325f, 0.07f), new PointF(0.94f, -0.02f), new PointF(0.94f, 0.13f), new PointF(0.325f, 0.22f) });
        p.FillMode = FillMode.Winding;
        notePath = p;
        return p;
    }

    static void DrawNote(Graphics g, PointF c, float ang, float pop, bool mirror)
    {
        var st = g.Save();
        float sc = NoteH * (1 + 0.16f * pop);
        g.TranslateTransform(c.X, c.Y - 3 * pop + NoteH * 0.5f);   // 아래 중심을 축으로 흔들기
        g.RotateTransform(ang);
        g.ScaleTransform(mirror ? -sc : sc, sc);
        g.TranslateTransform(-0.55f, -1f);
        var path = NotePath();
        // 네온 빛: 굵은 반투명 테두리 여러 겹 + 밝은 속
        for (int k = 4; k >= 1; k--)
            using (var pen = new Pen(Color.FromArgb((int)(26 + 22 * pop), NoteGlow), k * 0.07f) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, path);
        using (var b = new SolidBrush(NoteCore)) g.FillPath(b, path);
        g.Restore(st);
    }

    void Glow(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
    {
        using (var gb = new SolidBrush(Color.FromArgb(55, c)))
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (dx * dx + dy * dy <= 5 && (dx != 0 || dy != 0))
                        g.DrawString(s, f, gb, new RectangleF(r.X + dx, r.Y + dy, r.Width, r.Height), sf);
        using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
    }

    void DrawScreen(Graphics g)
    {
        var S = Skin.Screen;
        float x = S.X + 10, w = S.Width - 20, y = S.Y + 8;
        var center = new StringFormat { Alignment = StringAlignment.Center };
        var left = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        using (var f = F(26, FontStyle.Bold)) Glow(g, GameTitle, f, Neon, new RectangleF(S.X, y, S.Width, 34), center);
        y += 34;
        using (var f = F(14)) using (var b = new SolidBrush(Cream)) g.DrawString(PatchTitle, f, b, new RectangleF(S.X, y, S.Width, 20), center);
        y += 26;
        using (var p = new Pen(Color.FromArgb(110, Neon), 1)) g.DrawLine(p, x, y, x + w, y);
        y += 8;

        using (var fl = F(11)) using (var fv = F(12)) using (var bd = new SolidBrush(Dim)) using (var bc = new SolidBrush(Cream))
        {
            g.DrawString("게임 폴더", fl, bd, x, y); y += 16;
            var path = gameDir ?? "(찾지 못함)";
            var pf = new StringFormat { Trimming = StringTrimming.EllipsisPath };
            g.DrawString(path, fv, bc, new RectangleF(x, y, w, 34), pf);
            y += 36;
            g.DrawString("상태", fl, bd, x, y); y += 15;
        }
        using (var f = F(15, FontStyle.Bold)) using (var b = new SolidBrush(statusColor)) g.DrawString(statusText, f, b, new RectangleF(x, y, w, 22), left);
        y += 26;
        using (var p = new Pen(Color.FromArgb(110, Neon), 1)) g.DrawLine(p, x, y, x + w, y);
        y += 7;

        // 진행 기록 (긴 줄은 줄바꿈, 넘치면 최근 것만)
        float logBottom = S.Bottom - 38;
        using (var f = F(12))
        {
            var lines = new List<KeyValuePair<string, SizeF>>();
            foreach (var s in log)
            {
                var sz = g.MeasureString(s.Length == 0 ? " " : s, f, (int)w);
                lines.Add(new KeyValuePair<string, SizeF>(s, sz));
            }
            float total = 0; int start = lines.Count;
            while (start > 0 && total + lines[start - 1].Value.Height <= logBottom - y) { start--; total += lines[start].Value.Height; }
            float ly = y;
            for (int i = start; i < lines.Count; i++)
            {
                var s = lines[i].Key;
                Color c = s.StartsWith("!") ? Bad : s.StartsWith("──") ? Neon : (s.Contains("완료") ? Ok : Cream);
                using (var b = new SolidBrush(c)) g.DrawString(s, f, b, new RectangleF(x, ly, w, lines[i].Value.Height + 2));
                ly += lines[i].Value.Height;
            }
        }

        // 아래 줄: 진행 막대 / 버튼 설명 / 제작자
        float by = S.Bottom - 32;
        if (busy)
        {
            var bar = new RectangleF(x, by + 4, w, 6);
            using (var b = new SolidBrush(Color.FromArgb(60, Neon))) g.FillRectangle(b, bar);
            float seg = w * 0.3f, pos = (blink % 12) / 11f * (w - seg);
            using (var b = new SolidBrush(Neon)) g.FillRectangle(b, x + pos, by + 4, seg, 6);
        }
        else if (hover >= 0)
        {
            string hint = hover == SpeakerIdx ? (musicOn ? "배경음 끄기 (M)" : "배경음 켜기 (M)") : BtnHint[hover];
            using (var f = F(12)) using (var b = new SolidBrush(Warn)) g.DrawString(hint, f, b, new RectangleF(x, by, w, 18), left);
        }
        if (bgm != null) DrawSpeaker(g, SpeakerRect, musicOn, hover == SpeakerIdx);
    }

    static void DrawSpeaker(Graphics g, Rectangle r, bool on, bool hot)
    {
        Color c = hot ? Color.FromArgb(255, 215, 150) : (on ? Neon : Dim);
        float x = r.X, y = r.Y + r.Height / 2f;
        using (var path = new GraphicsPath())
        {
            // 스피커 몸통
            path.AddPolygon(new[] {
                new PointF(x + 1, y - 4), new PointF(x + 6, y - 4), new PointF(x + 12, y - 9),
                new PointF(x + 12, y + 9), new PointF(x + 6, y + 4), new PointF(x + 1, y + 4) });
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }
        using (var p = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            if (on)
            {
                g.DrawArc(p, x + 9, y - 5, 8, 10, -50, 100);     // 작은 물결
                g.DrawArc(p, x + 9, y - 9, 14, 18, -50, 100);    // 큰 물결
            }
            else
            {
                g.DrawLine(p, x + 16, y - 5, x + 23, y + 5);      // X
                g.DrawLine(p, x + 23, y - 5, x + 16, y + 5);
            }
        }
    }

    void DrawButtons(Graphics g)
    {
        var now = DateTime.Now;
        for (int i = 0; i < 5; i++)
        {
            var r = Skin.Faces[i];
            bool down = pressed == i && hover == i;
            if (down) using (var b = new SolidBrush(Color.FromArgb(70, 60, 20, 0))) g.FillRectangle(b, r);
            else if (hover == i) using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 230))) g.FillRectangle(b, r);
            using (var f = F(14, FontStyle.Bold))
            {
                var rr = new RectangleF(r.X, r.Y + (down ? 1 : 0), r.Width, r.Height);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var sh = new SolidBrush(Color.FromArgb(90, 255, 240, 200))) g.DrawString(BtnLabel[i], f, sh, new RectangleF(rr.X, rr.Y + 1, rr.Width, rr.Height), sf);
                using (var b = new SolidBrush(Color.FromArgb(95, 35, 12))) g.DrawString(BtnLabel[i], f, b, rr, sf);
            }

            // LED
            Color? c = null;
            if (ledUntil[i] > now) c = ledColor[i];
            if (busy && ledColor[i] == Warn && ledUntil[i] > now) c = (blink % 2 == 0) ? Warn : (Color?)null;
            if (c == null && hover == i) c = Color.FromArgb(255, 150, 60);
            if (c != null) DrawLed(g, Skin.Leds[i], c.Value);
        }
    }

    static void DrawLed(Graphics g, Point p, Color c)
    {
        for (int k = 4; k >= 1; k--)
        {
            float rr = 5 + k * 3;
            using (var b = new SolidBrush(Color.FromArgb(28, c))) g.FillEllipse(b, p.X - rr, p.Y - rr, rr * 2, rr * 2);
        }
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(p.X - 6, p.Y - 6, 12, 12);
            using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(255, Blend(c, Color.White, 0.55f)), SurroundColors = new[] { c } })
                g.FillPath(pb, path);
        }
    }
    static Color Blend(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    // ------------------------------------------------------------------ 레이어드 윈도우로 내보내기
    [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT pptDst, ref SZ psize, IntPtr hdcSrc, ref PT pptSrc, int crKey, ref BLEND pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    void PushToWindow(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(memDc, hBmp);
            var size = new SZ { cx = bmp.Width, cy = bmp.Height };
            var src = new PT { x = 0, y = 0 };
            var dst = new PT { x = Left, y = Top };
            var bl = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bl, 2);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            if (hBmp != IntPtr.Zero) { SelectObject(memDc, old); DeleteObject(hBmp); }
            DeleteDC(memDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public PT reserved, maxSize, maxPos, minTrack, maxTrack; }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0024 && SkinW > 0)          // WM_GETMINMAXINFO: 화면보다 커도 줄이지 않게
        {
            var mi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
            mi.maxTrack.x = Math.Max(mi.maxTrack.x, SkinW); mi.maxTrack.y = Math.Max(mi.maxTrack.y, SkinH);
            mi.minTrack.x = SkinW; mi.minTrack.y = SkinH;
            Marshal.StructureToPtr(mi, m.LParam, false);
            return;
        }
        if (m.Msg == 0x0046 && SkinW > 0)          // WM_WINDOWPOSCHANGING: 크기는 항상 그림 크기
        {
            var wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
            if ((wp.flags & 0x0001) == 0) { wp.cx = SkinW; wp.cy = SkinH; Marshal.StructureToPtr(wp, m.LParam, false); }
        }
        base.WndProc(ref m);
    }

    protected override void OnMove(EventArgs e) { base.OnMove(e); Render(); }
}


// 배경음: exe 옆의 bgm.mp3 (없으면 같은 폴더의 첫 mp3)를 윈도우 MCI로 반복 재생
class Bgm : IDisposable
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string cmd, System.Text.StringBuilder ret, int retLen, IntPtr cb);
    const string Alias = "kimjuke_bgm";
    public double Bpm = 103, OffsetMs = 0;   // 곡의 빠르기와 첫 박 위치(bgm.beat)
    bool playing;
    byte[] env;     // 20ms마다 소리 세기 (내장 곡일 때만)

    public static Bgm TryOpen(string dir)
    {
        try
        {
            // 항상 exe 안에 넣어 둔 곡만 재생 (같은 폴더의 다른 mp3는 무시)
            string f = ExtractEmbedded();
            byte[] env = ReadRes("bgm.env");
            byte[] beat = ReadRes("bgm.beat");
            if (f == null) return null;
            mciSendString("close " + Alias, null, 0, IntPtr.Zero);
            if (mciSendString("open \"" + f + "\" type mpegvideo alias " + Alias, null, 0, IntPtr.Zero) != 0) return null;
            mciSendString("setaudio " + Alias + " volume to 350", null, 0, IntPtr.Zero);
            mciSendString("set " + Alias + " time format milliseconds", null, 0, IntPtr.Zero);
            var bg = new Bgm { env = env };
            if (beat != null)
            {
                var parts = System.Text.Encoding.ASCII.GetString(beat).Split(' ');
                double v;
                if (parts.Length > 0 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) bg.Bpm = v;
                if (parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) bg.OffsetMs = v;
            }
            return bg;
        }
        catch { return null; }
    }

    static byte[] ReadRes(string name)
    {
        using (var st = typeof(Bgm).Assembly.GetManifestResourceStream(name))
        {
            if (st == null) return null;
            var b = new byte[st.Length]; st.Read(b, 0, b.Length); return b;
        }
    }

    public double PositionMs()
    {
        var sb = new System.Text.StringBuilder(32);
        if (mciSendString("status " + Alias + " position", sb, sb.Capacity, IntPtr.Zero) != 0) return 0;
        double v; return double.TryParse(sb.ToString(), out v) ? v : 0;
    }

    // 박자 지도가 있으면 그 값, 없으면 BPM으로 만든 박동
    public float EnergyAt(double ms)
    {
        if (env != null && env.Length > 0)
        {
            int i = (int)(ms / 20); if (i < 0) i = 0; if (i >= env.Length) i = env.Length - 1;
            return env[i] / 255f;
        }
        double beat = 60000.0 / Bpm, ph = (((ms - OffsetMs) % beat) + beat) % beat / beat;
        return (float)Math.Exp(-ph * 6);
    }

    static string ExtractEmbedded()
    {
        using (var st = typeof(Bgm).Assembly.GetManifestResourceStream("bgm.mp3"))
        {
            if (st == null) return null;
            var path = Path.Combine(Path.GetTempPath(), "kimjuke_bgm_psych_" + st.Length + ".mp3");
            if (!File.Exists(path) || new FileInfo(path).Length != st.Length)
                using (var fs = File.Create(path)) st.CopyTo(fs);
            return path;
        }
    }

    public void Play() { mciSendString("play " + Alias, null, 0, IntPtr.Zero); playing = true; }
    public void Pause() { mciSendString("pause " + Alias, null, 0, IntPtr.Zero); playing = false; }

    // 곡이 끝나면 처음부터 다시
    public void KeepLooping()
    {
        if (!playing) return;
        var sb = new System.Text.StringBuilder(32);
        mciSendString("status " + Alias + " mode", sb, sb.Capacity, IntPtr.Zero);
        if (sb.ToString() == "stopped")
        {
            mciSendString("seek " + Alias + " to start", null, 0, IntPtr.Zero);
            mciSendString("play " + Alias, null, 0, IntPtr.Zero);
        }
    }

    public void Dispose() { mciSendString("close " + Alias, null, 0, IntPtr.Zero); }
}
