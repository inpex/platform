﻿using System;

namespace CEFInjector.DirectXHook.Hook.Common
{
    public interface IOverlayElement : ICloneable
    {
        bool Hidden { get; set; }

        void Frame();
    }
}
