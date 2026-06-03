using UnityEngine;

namespace OPERATOR.Common.Settings
{
    /// <summary>
    /// Captures the current cursor lock/visibility state, forces the cursor
    /// unlocked and visible, and restores the captured state on <see cref="Restore"/>.
    /// </summary>
    public sealed class CursorScope
    {
        private readonly CursorLockMode _prevLockState;
        private readonly bool _prevVisible;

        public CursorScope()
        {
            _prevLockState = Cursor.lockState;
            _prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Restore the cursor state captured at construction time.</summary>
        public void Restore()
        {
            Cursor.lockState = _prevLockState;
            Cursor.visible = _prevVisible;
        }
    }
}
