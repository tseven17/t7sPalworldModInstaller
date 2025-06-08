// Program.cs – WinForms “t7’s Palworld Mod-Installer” v1.2.2
//
// * Non-self-contained build target (publish command shown in read-me)
// * No IL packers / compressors
// * Runtime-generated 64 × 64 icon (tiny, <2 KB resource)
// * Backup = copy-then-delete (never directory-move)
// * Process-kill happens **only after explicit user consent**
// * explorer.exe launches via UseShellExecute = true
//
// Publish (example):
//   dotnet publish -c Release -r win-x64 -p:PublishSingleFile=false -p:SelfContained=false
// Sign afterwards:
//   signtool sign /fd SHA256 /t http://timestamp.digicert.com /a ModInstaller_v122.exe

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

#nullable enable

namespace ModInstallerApp
{
    // ─────────────────────────  THEME  ─────────────────────────
    internal static class Theme
    {
        public static readonly Color Surface   = ColorTranslator.FromHtml("#1E1B2E");
        public static readonly Color Track     = ColorTranslator.FromHtml("#2A2740");
        public static readonly Color Accent    = ColorTranslator.FromHtml("#FF5DB1");
        public static readonly Color Accent2   = ColorTranslator.FromHtml("#11D0FF");
        public static readonly Color TextLight = ColorTranslator.FromHtml("#F2F2F2");
        public static readonly Color TextDark  = ColorTranslator.FromHtml("#1B1B1B");

        public static readonly Font BodyFont    = new("Segoe UI Semibold", 10F);
        public static readonly Font HeadingFont = new("Segoe UI Black", 18F);

        public static void Apply(Control c)
        {
            c.BackColor = Surface;
            c.ForeColor = TextLight;
            c.Font      = BodyFont;
            foreach (Control child in c.Controls) Apply(child);
        }
        public static Color Lighten(this Color c,int v)=>
            Color.FromArgb(c.A,
                Math.Clamp(c.R+v,0,255),
                Math.Clamp(c.G+v,0,255),
                Math.Clamp(c.B+v,0,255));
    }

    // ────────────────────  CUSTOM CONTROLS  ────────────────────
    public class ThemedButton : Button
    {
        bool _hover,_pressed;
        public ThemedButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Theme.Accent;   ForeColor = Theme.TextDark;
            Font = Theme.BodyFont;      Padding = new Padding(12,6,12,6);
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Cursor = Cursors.Hand; DoubleBuffered = true;
        }
        protected override void OnMouseEnter(EventArgs e){_hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){_hover=_pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){_pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){_pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = ClientRectangle;
            using var gp = new GraphicsPath();
            int rad=8;
            gp.AddArc(r.X,r.Y,rad,rad,180,90);gp.AddArc(r.Right-rad,r.Y,rad,rad,270,90);
            gp.AddArc(r.Right-rad,r.Bottom-rad,rad,rad,0,90);gp.AddArc(r.X,r.Bottom-rad,rad,rad,90,90);
            gp.CloseAllFigures();
            Color bg=_pressed?Theme.Accent2:_hover?BackColor.Lighten(25):BackColor;
            using var br=new SolidBrush(bg);e.Graphics.FillPath(br,gp);
            using var pen=new Pen(Color.FromArgb(90,Color.Black),1);e.Graphics.DrawPath(pen,gp);
            TextRenderer.DrawText(e.Graphics,Text,Font,r,ForeColor,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
    }
    public class ThemedProgressBar : ProgressBar
    {
        public ThemedProgressBar(){SetStyle(ControlStyles.UserPaint,true);BackColor=Theme.Track;ForeColor=Theme.Accent;}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Track);
            if(Maximum==0||Value==0) return;
            float pct=(float)Value/Maximum;
            using var br=new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(br,0,0,pct*Width,Height);
        }
    }

    // ──────────────────────  MAIN FORM  ───────────────────────
    public class InstallerForm : Form
    {
        const string VERSION      = "1.2.2";
        const string DRIVE_URL    = "https://drive.google.com/file/d/18NA2mcNTSZV6qkOFDHx69vISh4UN85Dk/view?usp=sharing";
        const string DISCORD_XD   = "https://discord.xdreamserver.com";
        const string DISCORD_GL   = "https://www.gladiate.net";
        const string MANIFEST     = "mod_manifest.json";
        const string VANILLA_PAK  = "Pal-Windows.pak";

        static readonly string CHANGELOG =
@"v1.2.2 – 2025-06-07
• Backup now always copy-then-delete (no directory moves) to avoid ransomware heuristics.
• explorer.exe launches via UseShellExecute = true for reputation.
• Process termination still optional (user prompt).

v1.2.1 / 1.2.0 – see previous notes.";

        static readonly string[] MOD_PATHS =
        {
            @"Pal\Binaries\Win64\mods",
            @"Pal\Binaries\Win64\ue4ss",
            @"Pal\Binaries\Win64\UE4SS_Signatures",
            @"Pal\Binaries\Win64\dwmapi.dll",
            @"Pal\Content\Paks\~mods",
            @"Pal\Content\Paks\LogicMods"
        };

        static readonly string BACKUP_ROOT =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         @"Downloads\Palworld Mods Backup");

        static readonly string DEFAULT_STEAM =
            @"C:\Program Files (x86)\Steam\steamapps\common";

        readonly TextBox           txtPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
        readonly ThemedProgressBar bar     = new() { Dock = DockStyle.Fill, Height = 22 };
        readonly RichTextBox       logBox  = new()
        {
            Dock     = DockStyle.Fill,
            ReadOnly = true,
            BackColor= Theme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas",9),
            ForeColor = Theme.TextLight
        };
        readonly TableLayoutPanel root = new();

        public InstallerForm()
        {
            Icon appIcon = CreateAppIcon();
            Icon = appIcon;

            Text = "t7's Palworld Mod-Installer";
            MinimumSize = new Size(740,560);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.Apply(this);

            BuildLayout(appIcon);

            void Center()=>root.Location=new Point(Math.Max(0,(ClientSize.Width-root.PreferredSize.Width )/2),
                                                   Math.Max(0,(ClientSize.Height-root.PreferredSize.Height)/2));
            Resize+=(_,__)=>Center();
            Shown +=(_,__)=>{Center();Log("Installer ready.");};
        }

        // ── UI builders ──
        LinkLabel MakeLink(string text,string url)
        {
            var ll=new LinkLabel{Text=text,AutoSize=true,LinkColor=Theme.Accent2,Margin=new Padding(0,0,15,0)};
            ll.LinkClicked+=(_,__)=>
                Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
            return ll;
        }
        void CopyTree(string src,string dst,string palRoot)
        {
            foreach(var f in Directory.GetFiles(src,"*",SearchOption.AllDirectories))
            {
                string relPal=Path.GetRelativePath(palRoot,f);
                string relSrc=Path.GetRelativePath(src,f);
                string dstFile=Path.Combine(dst,relSrc);
                Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                File.Copy(f,dstFile,true);
                Log($"Copy file {relPal}");
            }
        }
        void BuildLayout(Icon appIcon)
        {
            root.AutoSize=true;root.AutoSizeMode=AutoSizeMode.GrowAndShrink;root.Anchor=AnchorStyles.None;
            root.ColumnCount=1;root.RowCount=8;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,0));
            Controls.Add(root);

            var banner=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.LeftToRight,
                                           Padding=new Padding(8),WrapContents=false};
            banner.Controls.AddRange(new Control[]
            {
                new PictureBox{Image=appIcon.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Width=64,Height=64},
                new Label{Text="t7's Palworld Mod-Installer",Font=Theme.HeadingFont,AutoSize=true,Padding=new Padding(6,8,0,0)},
                new Label{Text=$"v{VERSION}",AutoSize=true,Padding=new Padding(8,22,0,0)}
            });
            root.Controls.Add(banner,0,0);

            var links=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.LeftToRight,
                                          Padding=new Padding(0,0,0,6)};
            links.Controls.Add(MakeLink("xDREAM MODPACK DOWNLOAD",DRIVE_URL));
            links.Controls.Add(MakeLink("xDREAM DISCORD",DISCORD_XD));
            links.Controls.Add(MakeLink("GLADIATE DISCORD",DISCORD_GL));
            root.Controls.Add(links,0,1);

            var pathRow=new TableLayoutPanel{ColumnCount=3,Dock=DockStyle.Fill,AutoSize=true};
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.Controls.Add(new Label{Text="Palworld.exe:",AutoSize=true,Padding=new Padding(0,6,6,0)},0,0);
            pathRow.Controls.Add(txtPath,1,0);
            var browse=new ThemedButton{Text="Browse…"};browse.Click+=(_,__)=>BrowsePal();
            pathRow.Controls.Add(browse,2,0);
            root.Controls.Add(pathRow,0,2);

            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,8,0,8)};
            actions.Controls.AddRange(new Control[]
            {
                MakeButton("Backup Mods",     BackupAsync),
                MakeButton("Install Modpack", InstallAsync),
                MakeButton("Verify Integrity",VerifyAsync),
                MakeButton("Restore Backup",  RollbackAsync),
                new ThemedButton{Text="Readme / About",AutoSize=true}
            });
            actions.Controls[^1].Click+=(_,__)=>
                ShowReadme();
            root.Controls.Add(actions,0,3);

            root.Controls.Add(bar,    0,4);
            root.Controls.Add(logBox, 0,5);

            var exitBtn=new ThemedButton{Text="Exit"};exitBtn.Click+=(_,__)=>Close();
            var exitRow=new Panel{Dock=DockStyle.Fill,Height=46};
            exitRow.Controls.Add(exitBtn);
            exitBtn.Location=new Point(exitRow.Width-exitBtn.Width-6,6);
            exitRow.Resize += (_,__) =>
                exitBtn.Location=new Point(exitRow.Width-exitBtn.Width-6,6);
            root.Controls.Add(exitRow,0,6);
        }

        // ── dialog util ──
        void ShowSimpleDialog(string title,string content,(string text,string url)[]? links=null)
        {
            using var dlg=new Form{Text=title,Icon=Icon,StartPosition=FormStartPosition.CenterParent,
                                   Width=660,Height=540,FormBorderStyle=FormBorderStyle.FixedDialog,
                                   MinimizeBox=false,MaximizeBox=false};
            Theme.Apply(dlg);

            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            dlg.Controls.Add(layout);

            var rtb=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.FixedSingle,
                                    BackColor=Theme.Surface,ForeColor=Theme.TextLight,
                                    Font=new Font("Consolas",9),Text=content};
            layout.Controls.Add(rtb,0,0);

            if(links is {Length:>0})
            {
                var lp=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,
                                           FlowDirection=FlowDirection.LeftToRight,
                                           Padding=new Padding(0,4,0,4)};
                foreach(var (txt,url) in links) lp.Controls.Add(MakeLink(txt,url));
                layout.Controls.Add(lp,0,1);
            }

            var close=new ThemedButton{Text="Close"};close.Click+=(_,__)=>dlg.Close();
            var bottom=new Panel{Dock=DockStyle.Fill,Height=46};
            bottom.Controls.Add(close);
            close.Location=new Point(bottom.Width-close.Width-6,6);
            bottom.Resize+=(_,__)=>close.Location=new Point(bottom.Width-close.Width-6,6);
            layout.Controls.Add(bottom,0,2);

            dlg.ShowDialog(this);
        }
        void ShowReadme()
        {
            var sb=new StringBuilder();
            sb.AppendLine("t7’s Palworld Mod-Installer");
            sb.AppendLine($"Version {VERSION}  –  Created June 7 2025");
            sb.AppendLine();
            sb.AppendLine("Publish command:");
            sb.AppendLine("dotnet publish -c Release -r win-x64 -p:PublishSingleFile=false -p:SelfContained=false");
            sb.AppendLine("signtool sign /fd SHA256 /t http://timestamp.digicert.com /a ModInstaller_v122.exe");
            sb.AppendLine();
            sb.AppendLine("Changelog:");
            sb.AppendLine(CHANGELOG);
            ShowSimpleDialog("Readme / About",sb.ToString(),
                new[]{("xDREAM MODPACK DOWNLOAD",DRIVE_URL),("xDREAM DISCORD",DISCORD_XD),("GLADIATE DISCORD",DISCORD_GL)});
        }

        // ── runtime icon ──
        static Icon CreateAppIcon()
        {
            const int S=64;using Bitmap bmp=new(S,S);
            using(Graphics g=Graphics.FromImage(bmp))
            {
                g.Clear(Theme.Accent);
                using var f  = new Font("Segoe UI Black",S*0.5f,FontStyle.Bold,GraphicsUnit.Pixel);
                var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString("P",f,Brushes.White,new RectangleF(0,0,S,S),sf);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        // ── logger ──
        void Log(string msg)
        {
            void append()
            {
                logBox.SelectionStart=logBox.TextLength;
                logBox.SelectionColor=Color.Gray;logBox.AppendText($"{DateTime.Now:HH:mm:ss} ");
                logBox.SelectionColor=Theme.TextLight;logBox.AppendText($"| {msg}{Environment.NewLine}");
                logBox.SelectionStart=logBox.TextLength;logBox.ScrollToCaret();
            }
            if(IsHandleCreated&&InvokeRequired)BeginInvoke((Action)append);else append();
        }

        // ── helpers ──
        void BrowsePal()
        {
            var dlg=new OpenFileDialog{InitialDirectory=Directory.Exists(DEFAULT_STEAM)?DEFAULT_STEAM:"",
                                       Filter="Palworld.exe|Palworld.exe"};
            if(dlg.ShowDialog()==DialogResult.OK) txtPath.Text=dlg.FileName;
        }
        bool PalPathOK()=>File.Exists(txtPath.Text)&&
            Path.GetFileName(txtPath.Text).Equals("Palworld.exe",StringComparison.OrdinalIgnoreCase);
        string PalRoot         => Path.GetDirectoryName(txtPath.Text)!;
        static bool PalRunning => Process.GetProcessesByName("Palworld").Length>0;
        static int  KillPal(){int k=0;foreach(var p in Process.GetProcessesByName("Palworld"))
                try{p.Kill(true);k++;}catch{}return k;}

        ThemedButton MakeButton(string label,Func<Task> handler)
        {
            var btn=new ThemedButton{Text=label};
            btn.Click+=async(_,__) =>
            {
                if(!PalPathOK()){MessageBox.Show("Select Palworld.exe first.");return;}
                try{await handler();}catch(Exception ex){MessageBox.Show(ex.ToString(),"Error");}
            };
            return btn;
        }
        static string Sha256Hex(string path)
        {
            using var sha=SHA256.Create();
            using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            var buf=new byte[1<<20];int n;while((n=fs.Read(buf,0,buf.Length))>0)sha.TransformBlock(buf,0,n,null,0);
            sha.TransformFinalBlock(Array.Empty<byte>(),0,0);
            return BitConverter.ToString(sha.Hash!).Replace("-","").ToLowerInvariant();
        }

        // ─── BACKUP (copy-then-delete) ──────────────────────────────────
        async Task BackupAsync()
        {
            await Task.Run(() =>
            {
                if(PalRunning&&MessageBox.Show("Palworld is running – close it?","Process",
                                               MessageBoxButtons.YesNo)==DialogResult.Yes)
                {
                    KillPal();Log("Process Palworld.exe killed.");
                }
                else if(PalRunning) return; // user declined

                string dstBase=Path.Combine(BACKUP_ROOT,DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

                foreach(var rel in MOD_PATHS)
                {
                    string src=Path.Combine(PalRoot,rel);
                    if(!File.Exists(src)&&!Directory.Exists(src)) continue;

                    string dst=Path.Combine(dstBase,rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                    if(File.Exists(src))
                    {
                        File.Copy(src,dst,true);
                        Log($"Copy file {rel}");
                        try{File.Delete(src);}catch{}
                        continue;
                    }

                    CopyTree(src,dst,PalRoot);
                    try{Directory.Delete(src,true);}catch{}
                    Log($"Copy folder {rel}");
                }

                // loose .pak files
                string paks=Path.Combine(PalRoot,@"Pal\Content\Paks");
                if(Directory.Exists(paks))
                    foreach(var f in Directory.GetFiles(paks))
                        if(!Path.GetFileName(f).Equals(VANILLA_PAK,StringComparison.OrdinalIgnoreCase))
                        {
                            string rel=Path.Combine(@"Pal\Content\Paks",Path.GetFileName(f));
                            string dst=Path.Combine(dstBase,rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                            File.Copy(f,dst,true);
                            Log($"Copy file {rel}");
                            try{File.Delete(f);}catch{}
                        }

                Log("Backup complete.");
                var psi=new ProcessStartInfo("explorer") { Arguments=$"/select,\"{dstBase}\"",UseShellExecute=true };
                Process.Start(psi);
            });
        }

        // ─── INSTALL (unchanged logic) ───────────────────────────────────
        async Task InstallAsync()
        {
            MessageBox.Show("Select any .zip that contains a 'Pal' folder inside.",
                            "Choose Mod-Pack ZIP",MessageBoxButtons.OK,MessageBoxIcon.Information);
            var ofd=new OpenFileDialog{Filter="ZIP files (*.zip)|*.zip|All files|*.*"};
            if(ofd.ShowDialog()!=DialogResult.OK)return;
            string zip=ofd.FileName;
            using var fsCheck=File.OpenRead(zip);
            if(!(fsCheck.ReadByte()==0x50&&fsCheck.ReadByte()==0x4B&&fsCheck.ReadByte()==0x03&&fsCheck.ReadByte()==0x04))
            {MessageBox.Show("Selected file is not a valid ZIP.");return;}

            await Task.Run(() =>
            {
                string tmp=zip+".tmpdir";if(Directory.Exists(tmp))Directory.Delete(tmp,true);
                ZipFile.ExtractToDirectory(zip,tmp);Log("Extracted ZIP.");
                string? srcPal=Directory.GetDirectories(tmp,"Pal",SearchOption.AllDirectories).FirstOrDefault();
                if(srcPal==null){Directory.Delete(tmp,true);MessageBox.Show("ZIP lacks a 'Pal' folder.");return;}
                string dstPal=Path.Combine(PalRoot,"Pal");
                var files=Directory.GetFiles(srcPal,"*",SearchOption.AllDirectories);
                Invoke(()=>{bar.Maximum=files.Length;bar.Value=0;});
                foreach(var s in files)
                {
                    string rel=Path.GetRelativePath(srcPal,s);
                    string dest=Path.Combine(dstPal,rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(s,dest,true);
                    Log($"Copy file {Path.Combine("Pal",rel)}");
                    Invoke(()=>bar.Value++);
                }
                Invoke(()=>bar.Value=0);Directory.Delete(tmp,true);
                SaveManifest();Log("Installation finished.");
            });
            MessageBox.Show("Mod-pack installed!");
        }

        // ─── MANIFEST SAVE (unchanged) ───────────────────────────────────
        void SaveManifest()
        {
            var dict=new Dictionary<string,string>();
            foreach(var f in Directory.GetFiles(Path.Combine(PalRoot,"Pal"),"*",SearchOption.AllDirectories))
            {
                if(Path.GetFileName(f).Equals(VANILLA_PAK,StringComparison.OrdinalIgnoreCase)) continue;
                dict[Path.GetRelativePath(PalRoot,f)] = Sha256Hex(f);
            }
            File.WriteAllText(Path.Combine(PalRoot,MANIFEST),
                JsonSerializer.Serialize(dict,new JsonSerializerOptions{WriteIndented=true}));
            Log("Manifest saved.");
        }

        // ─── VERIFY (unchanged) ──────────────────────────────────────────
        async Task VerifyAsync()
        {
            string mf=Path.Combine(PalRoot,MANIFEST);
            if(!File.Exists(mf)){MessageBox.Show("Manifest missing.");return;}

            var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(await File.ReadAllTextAsync(mf))!;
            bar.Maximum=manifest.Count;bar.Value=0;
            var changed=new List<string>();var missing=new List<string>();

            await Task.Run(()=>
            {
                foreach(var kv in manifest)
                {
                    string abs=Path.Combine(PalRoot,kv.Key);
                    if(!File.Exists(abs)) missing.Add(kv.Key);
                    else if(Sha256Hex(abs)!=kv.Value) changed.Add(kv.Key);
                    Invoke(()=>bar.Value++);
                }
            });
            bar.Value=0;

            if(changed.Count==0&&missing.Count==0)
            {
                MessageBox.Show("✅ Everything looks perfect!\nAll files match the original mod-pack.","Integrity Check");
                Log("Verify complete – no issues.");
                return;
            }

            var sb=new StringBuilder();
            sb.AppendLine("Integrity Report");
            sb.AppendLine("================");
            if(missing.Count>0)
            {
                sb.AppendLine($"❌ Missing files ({missing.Count}):");
                foreach(var f in missing.Take(25)) sb.AppendLine(" • "+f);
                if(missing.Count>25) sb.AppendLine($"   …and {missing.Count-25} more");
                sb.AppendLine();
            }
            if(changed.Count>0)
            {
                sb.AppendLine($"⚠️  Changed files ({changed.Count}):");
                foreach(var f in changed.Take(25)) sb.AppendLine(" • "+f);
                if(changed.Count>25) sb.AppendLine($"   …and {changed.Count-25} more");
                sb.AppendLine();
            }
            sb.AppendLine("What this means (ELI5):");
            sb.AppendLine("Some of the mod files on your computer don’t match what was");
            sb.AppendLine("installed. Games may crash or behave strangely.");
            sb.AppendLine();
            sb.AppendLine("How to fix:");
            sb.AppendLine("1. Click “Backup Mods” to save your current (broken) files.");
            sb.AppendLine("2. Click “Install Modpack” to reinstall clean files.");
            sb.AppendLine("   -- OR -- if you have a previous good backup, click “Restore Backup”.");
            ShowSimpleDialog("Integrity Report",sb.ToString());
            Log($"Verify complete – {changed.Count} changed, {missing.Count} missing.");
        }

        // ─── ROLLBACK (unchanged) ────────────────────────────────────────
        async Task RollbackAsync()
        {
            var fb=new FolderBrowserDialog{InitialDirectory=BACKUP_ROOT};
            if(fb.ShowDialog()!=DialogResult.OK) return;
            await BackupAsync();

            await Task.Run(() =>
            {
                foreach(var rel in MOD_PATHS)
                {
                    string abs=Path.Combine(PalRoot,rel);
                    if(Directory.Exists(abs))
                    {
                        foreach(var f in Directory.GetFiles(abs,"*",SearchOption.AllDirectories))
                            Log($"Delete file {Path.GetRelativePath(PalRoot,f)}");
                        Directory.Delete(abs,true);
                    }
                    else if(File.Exists(abs)){Log($"Delete file {rel}");File.Delete(abs);}
                }
                string paks=Path.Combine(PalRoot,@"Pal\Content\Paks");
                if(Directory.Exists(paks))
                    foreach(var f in Directory.GetFiles(paks))
                        if(!Path.GetFileName(f).Equals(VANILLA_PAK,StringComparison.OrdinalIgnoreCase))
                        {Log($"Delete file {Path.GetRelativePath(PalRoot,f)}");File.Delete(f);}
                var files=Directory.GetFiles(fb.SelectedPath,"*",SearchOption.AllDirectories);
                Invoke(()=>{bar.Maximum=files.Length;bar.Value=0;});
                foreach(var src in files)
                {
                    string rel=Path.GetRelativePath(fb.SelectedPath,src);
                    string dst=Path.Combine(PalRoot,rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src,dst,true);
                    Log($"Restore file {rel}");
                    Invoke(()=>bar.Value++);
                }
                Invoke(()=>bar.Value=0);
            });
            MessageBox.Show("Rollback complete.");Log("Rollback finished.");
        }
    }

    // ────────────────  ENTRY POINT  ────────────────
    internal static class Program
    {
        [STAThread]static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new InstallerForm());
        }
    }
}
