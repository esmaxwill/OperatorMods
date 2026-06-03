using System.Collections.Generic;
using UnityEngine;

namespace OPERATOR.Common
{
    public static class Players
    {
        public static PlayerMaster[] GetAllPlayers()
        {
            return UnityEngine.Object.FindObjectsOfType<PlayerMaster>();
        }
        public static PlayerMaster GetPlayerBySteamId(ulong steamId)
        {
            foreach (var player in Object.FindObjectsOfType<PlayerMaster>())
            {
                if (player.NetworkthisPlayerSteam64 == steamId)
                    return player;
            }
            return null;
        }
        public static List<PlayerMaster> GetPlayersWithMod(string modName)
        {
            var result = new List<PlayerMaster>();
            foreach (var sync in UnityEngine.Object.FindObjectsOfType<CharacterModSync>())
            {
                string modsJSON = sync.NetworkcharacterModsJSON;
                if (!string.IsNullOrEmpty(modsJSON) && modsJSON.Contains($"\"{modName}\""))
                    result.Add(sync.GetComponent<PlayerMaster>());
            }
            return result;
        }
    }
}
