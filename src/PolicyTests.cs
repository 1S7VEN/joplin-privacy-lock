using System;
static class PolicyTests {
    static WindowObservation[] W(int state) {return new[]{new WindowObservation(1,state)};}
    static void Check(bool ok,string label){if(!ok)throw new Exception("Policy test failed: "+label);}
    public static void Run() {
        var loaded=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<LockOptions>("{\"MinimizeEnabled\":false,\"TrayEnabled\":true,\"MinimizeDelaySeconds\":12,\"TrayDelaySeconds\":30,\"IdleMinutes\":0}");
        Check(!loaded.MinimizeEnabled&&loaded.TrayEnabled&&loaded.MinimizeDelaySeconds==12&&loaded.TrayDelaySeconds==30&&loaded.IdleMinutes==0,"JSON settings interchange");
        var o=new LockOptions();var p=new LockPolicy();
        Check(p.Evaluate(o,W(1),0,0,true)=="minimize","immediate minimize");
        o.MinimizeEnabled=false;p=new LockPolicy();
        Check(p.Evaluate(o,W(1),0,100,true)==null,"minimize disabled");
        Check(p.Evaluate(o,W(2),0,100,true)=="tray","tray independent");
        o.TrayEnabled=false;p=new LockPolicy();
        Check(p.Evaluate(o,W(2),0,100,true)==null,"tray disabled");
        o.MinimizeEnabled=true;o.MinimizeDelaySeconds=10;p=new LockPolicy();
        Check(p.Evaluate(o,W(1),0,0,true)==null,"delay starts");
        Check(p.Evaluate(o,W(1),0,9.9,true)==null,"before deadline");
        Check(p.Evaluate(o,W(1),0,10,true)=="minimize","at deadline");
        p=new LockPolicy();p.Evaluate(o,W(1),0,0,true);p.Evaluate(o,W(0),0,5,true);
        Check(p.Evaluate(o,W(1),0,12,true)==null,"restore cancels pending timer");
        o.TrayEnabled=true;o.TrayDelaySeconds=20;p=new LockPolicy();
        p.Evaluate(o,W(1),0,0,true);
        Check(p.Evaluate(o,W(2),0,9,true)==null,"tray transition resets timer");
        Check(p.Evaluate(o,W(2),0,28,true)==null,"tray delay independent");
        Check(p.Evaluate(o,W(2),0,29,true)=="tray","tray deadline");
        o.MinimizeEnabled=false;o.TrayEnabled=false;p=new LockPolicy();
        Check(p.Evaluate(o,W(0),299,0,true)==null,"idle before deadline");
        Check(p.Evaluate(o,W(0),300,0,true)=="idle","idle 5 minutes");
        Check(p.Evaluate(o,W(0),300,0,false)==null,"unlock grace");
        o.IdleMinutes=0;Check(p.Evaluate(o,W(0),999999,0,true)==null,"idle disabled");
        o.MinimizeEnabled=true;o.MinimizeDelaySeconds=0;p.Reset(W(1));
        Check(p.Evaluate(o,W(1),0,10,true)==null,"no relock of already minimized window");
        p.Evaluate(o,W(0),0,11,true);Check(p.Evaluate(o,W(1),0,12,true)=="minimize","new minimize rearms");
        o.MinimizeDelaySeconds=99;o.TrayDelaySeconds=99;p=new LockPolicy();p.Evaluate(o,W(1),0,0,true);
        o.MinimizeEnabled=false;p.Evaluate(o,W(1),0,100,true);o.MinimizeEnabled=true;
        Check(p.Evaluate(o,W(1),0,101,true)==null,"disable cancels timer and enable starts fresh");
        o.MinimizeDelaySeconds=-1;o.TrayDelaySeconds=999999;o.IdleMinutes=-10;o.Normalize();
        Check(o.MinimizeDelaySeconds==0&&o.TrayDelaySeconds==86400&&o.IdleMinutes==0,"range validation");
    }
}
