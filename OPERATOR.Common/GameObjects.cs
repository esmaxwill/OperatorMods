using System;
using UnityEngine;

namespace OPERATOR.Common
{
  [Serializable]
  public class NotFoundException : Exception
  {
    public NotFoundException()
    { }

    public NotFoundException(string message)
        : base(message)
    { }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    { }
  }

  static public class GameObjects
  {
    static public GameObject FindGameObject(string name)
    {
      var go = GameObject.Find(name) ?? throw new NotFoundException(string.Format("Could not find GameObject with name \"{0}\"", name));
      return go;
    }
  }
}
