using System.Collections.Generic;
using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// The recorded sounds in <c>Resources/Audio</c> (loaded once, cached). Every caller keeps a procedural
    /// fallback, so a missing file never means silence.
    /// </summary>
    public static class SfxLibrary
    {
        public const string Dispatch = "shipSendMission";
        public const string Success = "success";
        public const string Contact = "dradis";
        public const string HullImpact = "impact";
        public const string ShieldImpact = "impactshield";
        public const string Explosion = "explosion";
        public const string Laser = "laser";
        public const string HyperStart = "startHyperspace";
        public const string HyperBuild = "hyperportal";
        public const string Engine = "travelengine";
        public const string PortalOpen = "portalopen";
        public const string PortalIdle = "portalidl";
        public const string ShieldIdle = "shieldidl";
        public const string Click = "click";
        public const string Music = "eraSpace";

        static readonly Dictionary<string, AudioClip> Cache = new();

        public static AudioClip Get(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (Cache.TryGetValue(name, out var clip))
                return clip;
            clip = Resources.Load<AudioClip>("Audio/" + name);
            Cache[name] = clip;
            return clip;
        }
    }
}
