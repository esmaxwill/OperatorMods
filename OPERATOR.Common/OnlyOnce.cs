using System;

namespace OPERATOR.Common
{
    public class OnlyOnce
    {
        private bool _fired;

        public void Invoke(Action action)
        {
            if (_fired) return;
            _fired = true;
            action();
        }

        public void Reset() => _fired = false;
    }
}
