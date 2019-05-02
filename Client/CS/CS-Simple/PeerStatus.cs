using System; 

namespace Microsoft.SignalNow.Client
{
    public enum PeerStatus
    {
        Online = 0, 
        Offline, 
        Away,
        Busy,
        DoNotDisturb
    }
}