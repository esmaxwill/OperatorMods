using System.Collections.Generic;
using UnityEngine;

namespace OPERATOR.Common
{
  public static class CharacterMods
  {
    public static List<PlayerMaster> GetPlayersWithMod(string modName)
    {
      return Players.GetPlayersWithMod(modName);
    }
  }
}
