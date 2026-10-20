using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

static class Native {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder t, int n);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref InputInfo x);
    [StructLayout(LayoutKind.Sequential)] struct InputInfo { public uint size; public uint time; }
    public static double IdleSeconds() { var x=new InputInfo(); x.size=(uint)Marshal.SizeOf(x); return GetLastInputInfo(ref x) ? unchecked((uint)Environment.TickCount-x.time)/1000.0 : 0; }
}

sealed class PasswordStore {
    readonly string file;
    public PasswordStore(string dir) { file=Path.Combine(dir,"credential.dat"); }
    public bool Exists { get { return File.Exists(file); } }
    static byte[] Derive(string p, byte[] salt) { using(var k=new Rfc2898DeriveBytes(p,salt,120000,HashAlgorithmName.SHA256))return k.GetBytes(32); }
    public void Save(string p) {
        if(p.Length<4 || p.Length>256)throw new Exception("密码需要为 4–256 个字符。");
        var salt=new byte[16];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(salt);
        var hash=Derive(p,salt);var data=new byte[49];data[0]=1;Buffer.BlockCopy(salt,0,data,1,16);Buffer.BlockCopy(hash,0,data,17,32);
        var encrypted=ProtectedData.Protect(data,null,DataProtectionScope.CurrentUser);
        var temp=file+".tmp";File.WriteAllBytes(temp,encrypted);
        if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
    }
    public bool Verify(string p) {
        if(p==null || p.Length>256)return false;
        var data=ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser);
        if(data.Length!=49 || data[0]!=1)throw new Exception("密码校验数据损坏，请查看安装说明。");
        var salt=new byte[16];Buffer.BlockCopy(data,1,salt,0,16);var hash=Derive(p,salt);int diff=0;
        for(int i=0;i<32;i++)diff|=hash[i]^data[i+17];return diff==0;
    }
}

sealed class LockContext : ApplicationContext {
    readonly string dir, exe; readonly PasswordStore store;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly HashSet<IntPtr> tracked=new HashSet<IntPtr>(), hidden=new HashSet<IntPtr>();
    readonly Dictionary<uint,bool> processCache=new Dictionary<uint,bool>();
    readonly LockPolicy policy=new LockPolicy();
    LockOptions options=new LockOptions();
    Form dialog; bool locked, configuring, busy; int absent, failures; DateTime retryAt=DateTime.MinValue, graceUntil=DateTime.MinValue;
    public LockContext(string d,string e,string action) {
        dir=d;exe=e;store=new PasswordStore(dir);
        Scan();
        timer.Interval=200;timer.Tick+=Tick;timer.Start();
        if(store.Exists) { Lock(true); if(action=="configure"){} }
        else ShowDialog(true);
    }
    bool Matches(IntPtr h) {
        if(Native.GetWindow(h,4)!=IntPtr.Zero)return false;
        uint id;Native.GetWindowThreadProcessId(h,out id);
        bool match;
        if(!processCache.TryGetValue(id,out match)) {
            try { using(var p=Process.GetProcessById((int)id))match=String.Equals(p.MainModule.FileName,exe,StringComparison.OrdinalIgnoreCase); }
            catch {match=false;}
            processCache[id]=match;
        }
        if(!match)return false;
        var title=new StringBuilder(512);Native.GetWindowText(h,title,title.Capacity);
        return title.ToString().IndexOf("Joplin",StringComparison.OrdinalIgnoreCase)>=0;
    }
    void Scan() {
        tracked.RemoveWhere(h=>!Native.IsWindow(h));hidden.RemoveWhere(h=>!Native.IsWindow(h));
        Native.EnumWindows((h,p)=>{if((Native.IsWindowVisible(h)||Native.IsIconic(h))&&Matches(h))tracked.Add(h);return true;},IntPtr.Zero);
    }
    void ReadOptions() {
        try {
            var file=Path.Combine(dir,"options.json");
            if(File.Exists(file)) {var loaded=new JavaScriptSerializer().Deserialize<LockOptions>(File.ReadAllText(file));if(loaded!=null){loaded.Normalize();options=loaded;}}
            else {int n;if(Int32.TryParse(File.ReadAllText(Path.Combine(dir,"idle.txt")).Trim(),out n))options.IdleMinutes=Math.Max(0,n);}
        }catch{} // A partial write must not replace the last known settings.
    }
    WindowObservation[] Observe() {return tracked.Select(h=>new WindowObservation(h.ToInt64(),!Native.IsWindowVisible(h)?2:Native.IsIconic(h)?1:0)).ToArray();}
    void CoverRestored() {
        bool restored=false;
        foreach(var h in tracked.ToArray()) if(Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h)) {
            Native.ShowWindow(h,0);hidden.Add(h);restored=true;
        }
        if(restored)ShowDialog(false);
    }
    void Tick(object sender,EventArgs e) {
        if(busy)return;
        try {
            Scan();
            if(tracked.Count==0) {if(++absent>25){if(dialog!=null)dialog.Dispose();ExitThread();return;}}else absent=0;
            var commands=Path.Combine(dir,"commands");
            foreach(var f in Directory.GetFiles(commands,"*.request")) {
                var command=File.ReadAllText(f).Trim();File.Delete(f);
                if(command=="lock") {if(store.Exists)Lock(true);else ShowDialog(true);}
                if(command=="configure"&&!locked)ShowDialog(true);
                if(command=="configure"&&locked)ShowDialog(false);
            }
            if(!store.Exists)return;
            if(!locked) {
                ReadOptions();
                string reason=policy.Evaluate(options,Observe(),Native.IdleSeconds(),(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency,DateTime.UtcNow>graceUntil);
                if(reason!=null)Lock(reason=="idle");
            }
            if(locked)CoverRestored();
        }catch(Exception err){Log(err);}
    }
    void Lock(bool prompt) {
        if(!store.Exists)return;
        if(!locked) {
            locked=true;
            if(configuring&&dialog!=null){dialog.Dispose();dialog=null;configuring=false;}
        }
        // Lock silently. Keep a taskbar/tray entry so restoring Joplin is the
        // user's explicit request to see the password window.
        foreach(var h in tracked.ToArray())
            if(Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h))Native.ShowWindow(h,6);
    }
    void Unlock() {
        locked=false;graceUntil=DateTime.UtcNow.AddSeconds(3);
        if(dialog!=null){dialog.Dispose();dialog=null;}
        foreach(var h in hidden.ToArray())if(Native.IsWindow(h))Native.ShowWindow(h,9);
        hidden.Clear();
        policy.Reset(Observe());
    }
    void ShowDialog(bool setup) {
        if(dialog!=null&&!dialog.IsDisposed) {if(!setup&&configuring){dialog.Dispose();dialog=null;}else {if(dialog.WindowState==FormWindowState.Minimized)dialog.WindowState=FormWindowState.Normal;Native.ShowWindow(dialog.Handle,9);dialog.Activate();return;}}
        configuring=setup;
        var form=new Form {Text=setup?"设置 Joplin 锁定密码":"Joplin 已锁定",ClientSize=new Size(430,setup?420:250),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=!setup,ControlBox=setup,TopMost=true,Font=new Font("Microsoft YaHei UI",10),BackColor=Color.FromArgb(245,248,252)};
        dialog=form;
        var title=new Label {Text=setup?"设置或修改锁定密码":"输入密码以继续查看笔记",Left=25,Top=20,Width=380,Height=30,Font=new Font("Microsoft YaHei UI",13,FontStyle.Bold)};form.Controls.Add(title);
        int top=65;
        TextBox current=null,next=null,confirm=null;
        Func<string,TextBox> add=label=>{var l=new Label{Text=label,Left=25,Top=top,Width=370,Height=25};form.Controls.Add(l);var t=new TextBox{Left=25,Top=top+26,Width=380,Height=30,UseSystemPasswordChar=true,MaxLength=256};form.Controls.Add(t);top+=76;return t;};
        if(!setup||store.Exists)current=add(setup?"当前密码":"解锁密码");
        if(setup){next=add("新密码（至少 4 个字符）");confirm=add("再次输入新密码");}
        var message=new Label{Left=25,Top=top,Width=380,Height=42,ForeColor=Color.DarkRed};form.Controls.Add(message);
        var submit=new Button{Text=setup?"保存密码":"解锁",Left=25,Top=top+45,Width=380,Height=36};form.Controls.Add(submit);form.AcceptButton=submit;
        form.ClientSize=new Size(430,top+105);
        submit.Click+=(sender,args)=>{
            if(busy)return;
            if(DateTime.UtcNow<retryAt){message.Text="尝试次数较多，请等待 15 秒再重试。";return;}
            busy=true;submit.Enabled=false;
            try {
                if(store.Exists&&!store.Verify(current.Text)){if(++failures>=5)retryAt=DateTime.UtcNow.AddSeconds(15);message.Text="密码不正确。";current.Clear();return;}
                if(setup) {
                    if(next.Text!=confirm.Text){message.Text="两次输入的新密码不一致。";return;}
                    store.Save(next.Text);configuring=false;form.Dispose();dialog=null;graceUntil=DateTime.UtcNow.AddSeconds(3);
                }else Unlock();
                failures=0;retryAt=DateTime.MinValue;
            }catch(Exception err){message.Text=err.Message;Log(err);}
            finally{busy=false;if(!submit.IsDisposed)submit.Enabled=true;}
        };
        form.FormClosed+=(sender,args)=>{if(dialog==form)dialog=null;};
        form.Shown+=(sender,args)=>{if(current!=null)current.Focus();else next.Focus();};
        form.Show();
        // A hidden child-process startup can override the first ShowWindow call.
        // Explicitly show our own password form after WinForms creates its handle.
        Native.ShowWindow(form.Handle,5);
        form.Activate();
    }
    void Log(Exception e) {try{File.WriteAllText(Path.Combine(dir,"helper-error.txt"),DateTime.UtcNow.ToString("o")+" "+e.GetType().Name+": "+e.Message);}catch{}}
}

static class Program {
    [STAThread] static int Main(string[] args) {
        try {
            var a=new Dictionary<string,string>();for(int i=0;i+1<args.Length;i+=2)a[args[i]]=args[i+1];
            string dir=a["--data"],exe=a.ContainsKey("--exe")?a["--exe"]:"",action=a.ContainsKey("--action")?a["--action"]:"start";
            Directory.CreateDirectory(dir);Directory.CreateDirectory(Path.Combine(dir,"commands"));
            if(action=="self-test")return SelfTest(dir);
            string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(dir)))).Replace("-","");
            bool fresh;using(var mutex=new Mutex(true,"Local\\JoplinMinimizeLock-"+key,out fresh)) {
                if(!fresh){if(action!="start")File.WriteAllText(Path.Combine(dir,"commands",Guid.NewGuid().ToString("N")+".request"),action);return 0;}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new LockContext(dir,exe,action));mutex.ReleaseMutex();
            }
            return 0;
        }catch(Exception e){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"host-error.txt"),e.ToString());}catch{}return 1;}
    }
    static int SelfTest(string dir) {
        PolicyTests.Run();
        var s=new PasswordStore(dir);s.Save("5279");
        if(!s.Verify("5279")||s.Verify("5278"))throw new Exception("Four-digit password failed");
        bool rejected=false;try{s.Save("123");}catch{rejected=true;}if(!rejected)throw new Exception("Short password accepted");
        s.Save("test-local-only-123");
        if(!s.Verify("test-local-only-123")||s.Verify("wrong"))throw new Exception("Password validation failed");
        s.Save("second-test-only-456");
        if(!s.Verify("second-test-only-456")||s.Verify("test-local-only-123"))throw new Exception("Password update failed");
        if(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir,"credential.dat"))).Contains("second-test-only-456"))throw new Exception("Plaintext found");
        File.WriteAllText(Path.Combine(dir,"self-test-result.txt"),"PASS: lock policy settings and timers; four-digit password; short-password rejection; DPAPI storage; PBKDF2 verification; wrong-password rejection; password change; no plaintext password.");return 0;
    }
}
