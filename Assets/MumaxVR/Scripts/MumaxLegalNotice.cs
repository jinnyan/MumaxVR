// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: AGPL-3.0-or-later
// Additional attribution term (AGPL-3.0 section 7(b)): see NOTICE in the repository root.

using UnityEngine;

/// <summary>
/// Displays the copyright and license notice ("Appropriate Legal Notices",
/// AGPL-3.0 sections 0 and 5d) in the bottom-left corner of the desktop view,
/// next to the other MumaxVR panels.
///
/// Installs itself when the first scene loads, so no scene setup is needed.
/// Modified versions that are conveyed or offered over a network must keep
/// displaying these notices (AGPL-3.0 section 5d) and must preserve the author
/// attribution required by the additional term in NOTICE.
/// </summary>
public class MumaxLegalNotice : MonoBehaviour
{
    public const string Notice =
        "MumaxVR - Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake\n" +
        "Free software under the GNU AGPL-3.0-or-later: you may redistribute and modify it under that license.\n" +
        "It comes with ABSOLUTELY NO WARRANTY. License and source: github.com/jinnyan/MumaxVR";

    const float MaxWidth = 560f;
    const float Margin = 10f;

    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<MumaxLegalNotice>() != null) return;
        var go = new GameObject("MumaxVR Legal Notice");
        DontDestroyOnLoad(go);
        go.AddComponent<MumaxLegalNotice>();
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                fontSize = 11,
            };
        }

        var content = new GUIContent(Notice);
        float width = Mathf.Min(MaxWidth, Screen.width - 2f * Margin);
        float height = style.CalcHeight(content, width);
        GUI.Box(new Rect(Margin, Screen.height - height - Margin, width, height), content, style);
    }
}
