using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>Forge-themed palette. Status/element colours always ship with a second channel (icon letter/pattern) for colour-blind players.</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("14110f");
        public static readonly Color Panel = Hex("221c18");
        public static readonly Color PanelLight = Hex("2f2621");
        public static readonly Color Border = Hex("4a3b31");
        public static readonly Color Ember = Hex("e07a2f");
        public static readonly Color Gold = Hex("ffb347");
        public static readonly Color Text = Hex("f1e7dc");
        public static readonly Color Muted = Hex("a89a8c");
        public static readonly Color Danger = Hex("e05050");
        public static readonly Color Ok = Hex("6cd47a");
        public static readonly Color Link = Hex("7fd8ff");
        public static readonly Color Disabled = Hex("5a5048");
        public static readonly Color Shadow = new Color(0, 0, 0, 0.6f);

        public static Color Hex(string hex)
        {
            Color c;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        public static Color Rarity(Forgebound.Core.Content.Rarity r)
        {
            switch (r)
            {
                case Forgebound.Core.Content.Rarity.Uncommon: return Hex("6cd47a");
                case Forgebound.Core.Content.Rarity.Rare: return Hex("4fa3ff");
                case Forgebound.Core.Content.Rarity.Epic: return Hex("b46cff");
                case Forgebound.Core.Content.Rarity.Legendary: return Hex("ffb347");
                default: return Hex("c9bcae");
            }
        }

        public static Color DamageType(Forgebound.Core.Content.DamageType d)
        {
            switch (d)
            {
                case Forgebound.Core.Content.DamageType.Fire: return Hex("ff6a2a");
                case Forgebound.Core.Content.DamageType.Frost: return Hex("7fd8ff");
                case Forgebound.Core.Content.DamageType.Lightning: return Hex("ffe24a");
                case Forgebound.Core.Content.DamageType.Shadow: return Hex("9a6bff");
                case Forgebound.Core.Content.DamageType.Poison: return Hex("9be22d");
                case Forgebound.Core.Content.DamageType.Arcane: return Hex("4fa3ff");
                case Forgebound.Core.Content.DamageType.Radiant: return Hex("ffd76a");
                case Forgebound.Core.Content.DamageType.True: return Hex("ffffff");
                default: return Hex("f1e7dc");
            }
        }

        /// <summary>Single-letter glyph per status so status is never conveyed by colour alone.</summary>
        public static string StatusGlyph(Forgebound.Core.Content.StatusType s)
        {
            switch (s)
            {
                case Forgebound.Core.Content.StatusType.Burn: return "B";
                case Forgebound.Core.Content.StatusType.Chill: return "C";
                case Forgebound.Core.Content.StatusType.Freeze: return "F";
                case Forgebound.Core.Content.StatusType.Shock: return "S";
                case Forgebound.Core.Content.StatusType.Stun: return "!";
                case Forgebound.Core.Content.StatusType.ArmorBreak: return "A";
                case Forgebound.Core.Content.StatusType.Poison: return "P";
                case Forgebound.Core.Content.StatusType.Curse: return "X";
                case Forgebound.Core.Content.StatusType.Mark: return "M";
                case Forgebound.Core.Content.StatusType.Bleed: return "L";
                case Forgebound.Core.Content.StatusType.Weaken: return "W";
                case Forgebound.Core.Content.StatusType.Slow: return "~";
                case Forgebound.Core.Content.StatusType.Haste: return "H";
                case Forgebound.Core.Content.StatusType.Fortify: return "+";
                case Forgebound.Core.Content.StatusType.Regen: return "R";
                default: return "?";
            }
        }
    }

    /// <summary>Code-first uGUI builder. All screens are built at runtime (no hand-authored prefabs to keep in sync).</summary>
    public static class UiKit
    {
        public const float MinTouch = 56f;   // reference-resolution pixels ≈ 7-9 mm on phones at 1080p reference
        private static Font _font;
        public static float TextScale = 1f;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, float minX, float minY, float maxX, float maxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = Mathf.RoundToInt(size * TextScale);
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(10, Mathf.RoundToInt(size * TextScale * 0.6f));
            t.resizeTextMaxSize = Mathf.RoundToInt(size * TextScale);
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, Color? bg = null, int fontSize = 26)
        {
            var img = Panel(parent, "Button", bg ?? Theme.PanelLight);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.1f, 1.05f);
            colors.pressedColor = new Color(0.8f, 0.75f, 0.7f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            btn.colors = colors;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => { if (GameRoot.Services != null && GameRoot.Services.Audio != null) GameRoot.Services.Audio.Ui("ui_click"); onClick(); });
            var t = Label(img.transform, label, fontSize, Theme.Text, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 8, 4, 8, 4);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minHeight = MinTouch;
            le.minWidth = MinTouch;
            return btn;
        }

        public static void SetLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static VerticalLayoutGroup VBox(Transform parent, string name, float spacing = 8, int padding = 8, bool fitHeight = false)
        {
            var rt = Rect(parent, name);
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlHeight = true;
            g.childControlWidth = true;
            g.childForceExpandHeight = false;
            g.childForceExpandWidth = true;
            if (fitHeight) rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return g;
        }

        public static HorizontalLayoutGroup HBox(Transform parent, string name, float spacing = 8, int padding = 0)
        {
            var rt = Rect(parent, name);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childControlHeight = true;
            g.childControlWidth = true;
            g.childForceExpandHeight = true;
            g.childForceExpandWidth = true;
            return g;
        }

        public static LayoutElement Size(Component c, float minW = -1, float minH = -1, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            // Explicit == (not ??): Unity objects use an overloaded null check that ?? bypasses.
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (minW >= 0) le.minWidth = minW;
            if (minH >= 0) le.minHeight = minH;
            if (prefW >= 0) le.preferredWidth = prefW;
            if (prefH >= 0) le.preferredHeight = prefH;
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        /// <summary>Vertical scroll list; returns the content transform to add rows to.</summary>
        public static RectTransform ScrollList(Transform parent, string name, out ScrollRect scroll)
        {
            var root = Panel(parent, name, new Color(0, 0, 0, 0.15f));
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30;
            var viewport = Panel(root.transform, "Viewport", new Color(1, 1, 1, 0.01f));
            Stretch(viewport.rectTransform);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = VBox(viewport.transform, "Content", 6, 6);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.rectTransform;
            scroll.content = crt;
            return crt;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>A horizontal fill bar (HP, cooldown, damage share).</summary>
        public static Image Bar(Transform parent, Color fill, Color back, out RectTransform root)
        {
            var bg = Panel(parent, "Bar", back);
            root = bg.rectTransform;
            var f = Panel(bg.transform, "Fill", fill);
            f.rectTransform.anchorMin = Vector2.zero;
            f.rectTransform.anchorMax = new Vector2(1, 1);
            f.rectTransform.offsetMin = Vector2.zero;
            f.rectTransform.offsetMax = Vector2.zero;
            return f;
        }

        public static void SetFill(Image fill, float t)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(t), 1);
        }

        /// <summary>A UI line segment between two local points (used for board links and route edges).</summary>
        public static Image Line(Transform parent, Vector2 a, Vector2 b, float width, Color color)
        {
            var img = Panel(parent, "Line", color);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0.5f);
            var d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude, width);
            rt.anchoredPosition = a;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return img;
        }

        public static Image Icon(Transform parent, Sprite sprite, Color tint, float size)
        {
            var img = Panel(parent, "Icon", tint);
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            Size(img, size, size, size, size);
            return img;
        }

        /// <summary>Attach hover (desktop) / long-press (touch) tooltip text produced lazily.</summary>
        public static void Tooltip(GameObject go, Func<string> text)
        {
            var t = go.GetComponent<TooltipTrigger>();
            if (t == null) t = go.AddComponent<TooltipTrigger>();
            t.Provider = text;
        }
    }

    public sealed class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Func<string> Provider;
        private float _downAt = -1;

        public void OnPointerEnter(PointerEventData e)
        {
            if (Input.touchCount == 0 && Provider != null && UiRoot.Instance != null) UiRoot.Instance.ShowTooltip(Provider(), e.position);
        }

        public void OnPointerExit(PointerEventData e) { if (UiRoot.Instance != null) UiRoot.Instance.HideTooltip(); }
        public void OnPointerDown(PointerEventData e) { _downAt = Time.unscaledTime; }
        public void OnPointerUp(PointerEventData e)
        {
            // Long-press on touch shows the tooltip (tap still activates the control).
            if (_downAt > 0 && Time.unscaledTime - _downAt > 0.45f && Provider != null && UiRoot.Instance != null) UiRoot.Instance.ShowTooltip(Provider(), e.position);
            _downAt = -1;
        }

        private void OnDisable() { if (UiRoot.Instance != null) UiRoot.Instance.HideTooltip(); }
    }
}
