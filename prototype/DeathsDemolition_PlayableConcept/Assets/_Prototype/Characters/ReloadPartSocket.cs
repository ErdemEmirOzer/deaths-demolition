using UnityEngine;
namespace DD.Prototype.Characters
{
    /// <summary>Only the AP-024 TD body's socket carries its calibrated part frame; FP sockets retain their own contract.</summary>
    [DisallowMultipleComponent]
    public sealed class ReloadPartSocket : MonoBehaviour
    {
        public string Key { get; set; }
    }
}
