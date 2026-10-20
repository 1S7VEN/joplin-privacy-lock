using System;
using System.Collections.Generic;
using System.Linq;

sealed class LockOptions {
    public bool MinimizeEnabled { get; set; }
    public bool TrayEnabled { get; set; }
    public int MinimizeDelaySeconds { get; set; }
    public int TrayDelaySeconds { get; set; }
    public int IdleMinutes { get; set; }
    public LockOptions() { MinimizeEnabled=true;TrayEnabled=true;IdleMinutes=5; }
    public void Normalize() {
        MinimizeDelaySeconds=Math.Max(0,Math.Min(86400,MinimizeDelaySeconds));
        TrayDelaySeconds=Math.Max(0,Math.Min(86400,TrayDelaySeconds));
        IdleMinutes=Math.Max(0,Math.Min(1440,IdleMinutes));
    }
}
// State: 0 normal, 1 minimized, 2 hidden in tray. Window restoration cancels its timer.
sealed class WindowObservation {
    public long Id; public int State;
    public WindowObservation(long id,int state) {Id=id;State=state;}
}
sealed class LockPolicy {
    sealed class Pending {public int State;public double Since;}
    readonly Dictionary<long,Pending> pending=new Dictionary<long,Pending>();
    readonly Dictionary<long,int> ignored=new Dictionary<long,int>();
    public void Reset(IEnumerable<WindowObservation> current) {
        pending.Clear();ignored.Clear();foreach(var w in current)if(w.State!=0)ignored[w.Id]=w.State;
    }
    public string Evaluate(LockOptions o,IEnumerable<WindowObservation> observations,double idle,double now,bool allowIdle) {
        var windows=observations.ToArray();var present=new HashSet<long>(windows.Select(w=>w.Id));
        foreach(var id in pending.Keys.Where(k=>!present.Contains(k)).ToArray())pending.Remove(id);
        foreach(var id in ignored.Keys.Where(k=>!present.Contains(k)).ToArray())ignored.Remove(id);
        foreach(var w in windows) {
            int state;
            if(ignored.TryGetValue(w.Id,out state)) {if(state==w.State)continue;ignored.Remove(w.Id);}
            bool enabled=w.State==1?o.MinimizeEnabled:w.State==2?o.TrayEnabled:false;
            if(!enabled){pending.Remove(w.Id);continue;}
            Pending entry;
            if(!pending.TryGetValue(w.Id,out entry)||entry.State!=w.State){entry=new Pending{State=w.State,Since=now};pending[w.Id]=entry;}
            int delay=w.State==1?o.MinimizeDelaySeconds:o.TrayDelaySeconds;
            if(now-entry.Since>=delay)return w.State==1?"minimize":"tray";
        }
        return allowIdle&&o.IdleMinutes>0&&idle>=o.IdleMinutes*60?"idle":null;
    }
}
