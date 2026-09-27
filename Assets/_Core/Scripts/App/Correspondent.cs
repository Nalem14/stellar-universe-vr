using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Utils;
using Newtonsoft.Json.Linq;

namespace Core.App
{
    /// <summary>Who is on the other end of a transmission (a player, or the game's own system mail).</summary>
    public sealed class Correspondent
    {
        /// <summary>users.id of the sender (0 = system mail).</summary>
        public int UserId;
        public string Username = string.Empty;
        /// <summary>Last words received (message, or mail subject).</summary>
        public string Text = string.Empty;
        public bool IsMail;
        public float At;

        public bool IsSystem => UserId <= 0;

        static readonly Dictionary<int, JObject> Empires = new();
        static readonly HashSet<int> Pending = new();

        /// <summary>Cached GetEmpire of <paramref name="userId"/> (null until read, or without an empire).</summary>
        public static JObject EmpireOf(int userId) => Empires.TryGetValue(userId, out var e) ? e : null;

        /// <summary>Read the sender's empire once (flag, leader, name) — GetEmpire user=…</summary>
        public static async Task<JObject> LoadEmpire(int userId)
        {
            if (userId <= 0)
                return null;
            if (Empires.TryGetValue(userId, out var cached))
                return cached;
            if (!Pending.Add(userId))
                return null;
            try
            {
                var res = await ActionJs.Get("GetEmpire", new Dictionary<string, string> { { "user", userId.ToString() } });
                if (!res.Ok || string.IsNullOrEmpty(res.Body) || res.Body[0] != '{')
                    return null;
                var o = JObject.Parse(res.Body);
                Empires[userId] = o;
                return o;
            }
            catch
            {
                return null;
            }
            finally
            {
                Pending.Remove(userId);
            }
        }
    }
}
