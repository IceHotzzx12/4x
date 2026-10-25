#if UNITY_EDITOR

using UnityEngine;

namespace Unity.FantasyKingdom
{
    /// <summary>
    /// Sample project verifier disabled for Fantasy 4x integration.
    /// The stock script forced Assets/Data paths and overwrote this project's URP/Input settings.
    /// Outer map uses OpenRTS + project URP; see FantasyKingdomOuterMapSetup.
    /// </summary>
    public class VerifyProject : ScriptableObject
    {
        // Intentionally empty — do not re-enable InitializeOnLoad here.
    }
}

#endif
