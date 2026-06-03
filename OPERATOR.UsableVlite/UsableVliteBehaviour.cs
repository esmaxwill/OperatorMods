using System.Collections.Generic;
using MessagePack;
using OPERATOR.Common;
using OPERATOR.Common.Networking;
using UnityEngine;

namespace OPERATOR.UsableVlite
{
    // The per-player custom V-Lite color, sent over NetMessenger. Attribute-based MessagePack
    // contract (the default resolver requires [MessagePackObject]/[Key]).
    [MessagePackObject]
    public struct VLiteColorReceived
    {
        [Key(0)] public float R;
        [Key(1)] public float G;
        [Key(2)] public float B;
    }

    public class UsableVliteBehaviour : MonoBehaviour
    {
        public UsableVliteBehaviour(System.IntPtr ptr) : base(ptr) { }

        private const string MOD_NAME = "V-Lite";
        private const string LIGHT_NAME = "OPERATOR_VLite_Light";

        // Latest custom color each player has broadcast, keyed by Steam64. Applied to their light.
        private static readonly Dictionary<ulong, Color> _colorBySteam = new Dictionary<ulong, Color>();
        private static bool _subscribed;

        private bool _scanning;
        private bool _announced;

        private void OnEnable()
        {
            // Receive others' colors. stateful => the server caches each player's latest color and
            // replays it to clients that join later, so colors set before you joined still show.
            NetMessenger.Register<VLiteColorReceived>(OnVLiteColorReceived, stateful: true);

            // Broadcast my color whenever I change it in the settings menu (subscribe once; the
            // ConfigEntry + its event are static).
            if (!_subscribed)
            {
                _subscribed = true;
                UsableVlitePlugin.MyCustomColor.SettingChanged += OnMyColorChanged;
            }
        }

        // Named handler (not a lambda): subscribing a lambda to BepInEx's nullable-annotated
        // EventHandler trips CS0656 in this project (no NullableAttribute in its reference set).
        private static void OnMyColorChanged(object sender, System.EventArgs e) => BroadcastMyColor();

        private void Update()
        {
            if (_scanning)
            {
                if (GameManager.myPlayerNetworking == null)
                {
                    CancelInvoke(nameof(AttachAllLights));
                    _scanning = false;
                    _announced = false;
                }
                return;
            }

            if (GameManager.myPlayerNetworking == null) return;

            _scanning = true;
            InvokeRepeating(nameof(AttachAllLights), UsableVlitePlugin.InitialDelay.Value, UsableVlitePlugin.ScanInterval.Value);

            // Announce my current color once per spawn so peers pick it up even if I never change it
            // this session (the stateful cache also covers anyone who joins after).
            if (!_announced)
            {
                _announced = true;
                BroadcastMyColor();
            }
        }

        private static void BroadcastMyColor()
        {
            var c = UsableVlitePlugin.MyCustomColor.Value;
            NetMessenger.Broadcast(new VLiteColorReceived { R = c.r, G = c.g, B = c.b });
        }

        private void OnVLiteColorReceived(Envelope<VLiteColorReceived> message)
        {
            var c = new Color(message.payload.R, message.payload.G, message.payload.B);
            _colorBySteam[message.senderSteamId] = c;

            // We don't attach our own light locally (others render us), so nothing to apply for self.
            if (message.fromSelf) return;

            // If the sender's light already exists, recolor it now; otherwise the next AttachAllLights
            // scan will read the cached color via GetColorForPlayer.
            var pm = Players.GetPlayerBySteamId(message.senderSteamId);
            if (pm != null) ApplyColorToLight(pm, c);
        }

        private void AttachAllLights()
        {
            var players = CharacterMods.GetPlayersWithMod(MOD_NAME);
            UsableVlitePlugin.Logger.LogDebug(string.Format(
                "UsableVlite: {0} player(s) detected with '{1}' equipped.", players.Count, MOD_NAME));

            foreach (var pm in players)
            {
                if (pm == null) { UsableVlitePlugin.Logger.LogDebug("  skip: null PlayerMaster"); continue; }

                string who = string.Format("'{0}' '{1}' (JoinIndex={2})", pm.NetworkthisPlayerName, pm.NetworkthisPlayerSteam64, pm.UniqueID);

                if (pm == PlayerMaster.MyPlayerMaster)
                { UsableVlitePlugin.Logger.LogDebug("  skip " + who + ": local player"); continue; }

                var pn = pm.PlayerSpawnedObject;
                if (pn == null)
                { UsableVlitePlugin.Logger.LogDebug("  skip " + who + ": no spawned object"); continue; }

                if (pn.health != null && pn.health.isBot)
                { UsableVlitePlugin.Logger.LogDebug("  skip " + who + ": is bot"); continue; }

                var anchor = FindVLiteAnchor(pn);
                if (anchor == null)
                { UsableVlitePlugin.Logger.LogDebug("  skip " + who + ": no V-LITE anchor found on model"); continue; }

                var existing = anchor.Find(LIGHT_NAME);
                if (existing != null)
                {
                    // Already attached — keep its color in sync with the latest received value.
                    var lc = existing.GetComponent<Light>();
                    if (lc != null) lc.color = GetColorForPlayer(pm, pm.UniqueID);
                    UsableVlitePlugin.Logger.LogDebug("  " + who + ": light already present (color refreshed)");
                    continue;
                }

                CreateLight(pm, anchor, pm.UniqueID);
                UsableVlitePlugin.Logger.LogDebug("  attached light to " + who);
            }
        }

        // Recolor a player's existing V-Lite light immediately (used when a color arrives over the network).
        private static void ApplyColorToLight(PlayerMaster pm, Color c)
        {
            var pn = pm.PlayerSpawnedObject;
            if (pn == null) return;
            var anchor = FindVLiteAnchor(pn);
            var lightTf = anchor != null ? anchor.Find(LIGHT_NAME) : null;
            if (lightTf == null) return;
            var light = lightTf.GetComponent<Light>();
            if (light != null) light.color = c;
        }

        private static Transform FindVLiteAnchor(PlayerNetworking pn)
        {
            var modParents = pn.GetComponentsInChildren<CharacterModParent>(true);
            for (int i = 0; i < modParents.Length; i++)
            {
                var mp = modParents[i];
                if (mp == null || mp.currentMod == null) continue;
                if (mp.currentMod.name.Contains(MOD_NAME))
                    return mp.transform;
            }
            return null;
        }

        private static Color GetColorForPlayer(PlayerMaster player, int uniqueID)
        {
            // If the player master doesn't exist just make them white
            if (player == null)
            {
                return Color.white;
            }

            // 1) A custom color this player broadcast over the network takes priority.
            if (_colorBySteam.TryGetValue(player.NetworkthisPlayerSteam64, out var custom))
            {
                return custom;
            }

            var palette = UsableVlitePlugin.JoinColors;

            // 2) Name override -> palette index.
            var name = player.NetworkthisPlayerName;
            if (UsableVlitePlugin.NameColorOverrides.ContainsKey(name))
            {
                int idx = UsableVlitePlugin.NameColorOverrides[name].Value;
                return idx >= 0 && idx < palette.Length ? palette[idx].Value : Color.white;
            }

            // 3) Fall back to the join index.
            return uniqueID < palette.Length ? palette[uniqueID].Value : Color.white;
        }

        private static void CreateLight(PlayerMaster player, Transform anchor, int joinIndex)
        {
            // It doesn't exist so let's create it.
            var go = new GameObject(LIGHT_NAME);
            go.transform.SetParent(anchor, false);


            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = GetColorForPlayer(player, joinIndex);
            light.intensity = 0.1f;
            light.range = 0.1f;
            light.shadows = LightShadows.None;

        }
    }
}
