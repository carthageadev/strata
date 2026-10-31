using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Strata
{
    [Overlay(typeof(SceneView), "strata-terrain", "Strata", true, defaultDockZone = DockZone.Floating, defaultLayout = Layout.Panel)]
    public sealed class StrataOverlay : Overlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(StrataTool);

        VisualElement root;
        readonly List<Action> refreshers = new List<Action>();
        StrataBrush builtFor = (StrataBrush)(-1);
        StrataTerrain builtTerrain;
        int builtStamp = -1;
        string builtShape = "";

        static string Shape() => $"{StrataSettings.ApplyHeight}{StrataSettings.ApplyTop}{StrataSettings.ApplySides}{StrataSettings.StairType}";

        static readonly Color Accent = new Color(0.95f, 0.72f, 0.3f);

        public override void OnCreated()
        {
            displayedChanged += _ => KeepInside();
            floatingChanged += _ => KeepInside();
        }

        void KeepInside()
        {
            if (containerWindow == null) return;
            if (!floating) { Undock(); if (!floating) return; }
            var win = containerWindow.position;
            var p = floatingPosition;
            float w = Mathf.Max(250f, size.x), h = Mathf.Max(120f, size.y);
            if (p == Vector2.zero) p = new Vector2(win.width - w - 8, 30);
            if (p.x + w > win.width - 6) p.x = Mathf.Max(6f, win.width - w - 6);
            if (p.y + h > win.height - 6) p.y = Mathf.Max(24f, win.height - h - 6);
            if (p.x < 6) p.x = 6;
            if (p.y < 24) p.y = 24;
            if (p != floatingPosition) floatingPosition = p;
            if (root != null) root.style.maxHeight = Mathf.Max(220f, win.height - p.y - 56f);
        }

        public override VisualElement CreatePanelContent()
        {
            root = new VisualElement();
            root.style.width = 244;
            root.style.flexDirection = FlexDirection.Column;
            root.schedule.Execute(KeepInside).Every(500);
            root.style.paddingLeft = 6; root.style.paddingRight = 6; root.style.paddingTop = 4; root.style.paddingBottom = 6;
            StrataSessions.Changed += OnChanged;
            root.RegisterCallback<DetachFromPanelEvent>(_ => StrataSessions.Changed -= OnChanged);
            Build();
            return root;
        }

        void OnChanged()
        {
            if (root == null) return;
            var a = StrataSessions.Active;
            if (builtFor != StrataSettings.Brush || builtTerrain != a || builtShape != Shape() || (a != null && builtStamp != Formats.StrataProfiles.Stamp(a))) Build();
            else Refresh();
        }

        void Refresh() { foreach (var r in refreshers) r(); }

        void Build()
        {
            root.Clear();
            refreshers.Clear();
            builtFor = StrataSettings.Brush;
            var t = builtTerrain = StrataSessions.Active;
            builtStamp = t != null ? Formats.StrataProfiles.Stamp(t) : -1;
            builtShape = Shape();
            if (t == null || !t.Connected)
            {
                root.Add(Note(t == null ? "No terrain here." : "This terrain is not in the open scene."));
                var none = Row();
                var make = Button("New terrain…", StrataNewTerrain.Open, "Make a terrain in this scene (also GameObject > Strata > Terrain)");
                var open = Button("Open…", () => StrataWindow.ShowWindow(), "The Strata window: open a terrain from the project");
                make.style.flexGrow = open.style.flexGrow = 1;
                none.Add(make); none.Add(open);
                root.Add(none);
                return;
            }
            root.Add(BrushBar());
            var scroller = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroller.style.flexShrink = 1;
            scroller.style.marginTop = 6;
            var body = scroller.contentContainer;
            root.Add(scroller);
            var p = StrataPreview.Palette(t);
            switch (StrataSettings.Brush)
            {
                case StrataBrush.Level:
                    body.Add(ApplyRow());
                    if (StrataSettings.ApplyHeight) body.Add(LevelRow());
                    if (StrataSettings.ApplyTop) body.Add(Layers(p));
                    if (StrataSettings.ApplySides) body.Add(StyleRows(p));
                    body.Add(SizeRows());
                    break;
                case StrataBrush.Paint:
                    body.Add(Layers(p));
                    body.Add(Slider("Radius", 0.5f, 16f, () => StrataSettings.Radius, v => StrataSettings.Radius = v, "0.0 m", "Brush radius ([ and ])"));
                    body.Add(Slider("Strength", 0.05f, 1f, () => StrataSettings.Strength, v => StrataSettings.Strength = v, "0.00"));
                    body.Add(Slider("Edge", 0f, 1f, () => StrataSettings.Hardness, v => StrataSettings.Hardness = v, "0.00", "How much of the brush paints at full strength"));
                    break;
                case StrataBrush.Stairs:
                {
                    var types = p.stairs.Select(s => (s.index, s.name)).ToArray();
                    int type = p.stairs.Any(s => s.index == StrataSettings.StairType) ? StrataSettings.StairType : (p.stairs.Length > 0 ? p.stairs[0].index : 0);
                    body.Add(Chips("Type", types, () => type, v => StrataSettings.StairType = v, rebuild: true));
                    var sides = p.SidesLabel(type);
                    if (sides != null) body.Add(Toggle(sides, "Along the sides of the stair", () => StrataSettings.CheekWalls, v => StrataSettings.CheekWalls = v));
                    var climb = Chips("Height", new[] { (0, "Auto"), (1, "Custom") }, () => StrataSettings.StairCustom ? 1 : 0, v => StrataSettings.StairCustom = v == 1,
                        tips: new[] { "Climbs to meet the ground beyond its head", "Climbs this far from its foot (it can end against a wall)" });
                    var rise = new FloatField { tooltip = "How high it climbs, in metres (steps of 0.25)" };
                    rise.style.width = 48; rise.style.marginLeft = 2;
                    rise.RegisterValueChangedCallback(e => { StrataSettings.StairRise = Mathf.Max(0.25f, Mathf.Round(e.newValue * 4f) / 4f); SceneView.RepaintAll(); });
                    rise.RegisterCallback<FocusOutEvent>(_ => rise.SetValueWithoutNotify(StrataSettings.StairRise));
                    climb[1].Add(rise);
                    climb[1].Add(new Label("m") { style = { marginLeft = 2, fontSize = 11 } });
                    refreshers.Add(() =>
                    {
                        rise.style.display = StrataSettings.StairCustom ? DisplayStyle.Flex : DisplayStyle.None;
                        if (rise.focusController?.focusedElement != rise) rise.SetValueWithoutNotify(StrataSettings.StairRise);
                    });
                    body.Add(climb);
                    break;
                }
                case StrataBrush.Shape:
                    body.Add(Chips("Shape", new[] { (0, "Valley"), (1, "Hill"), (4, "Crater"), (2, "Ramp"), (3, "Smooth") }, () => (int)StrataSettings.Shape, v => StrataSettings.Shape = (ShapeProfile)v,
                        tips: new[] { "A chasm along the long side", "A rounded hill", "A bowl with a raised lip", "From one end's height to the other's", "Smooths what is there" }));
                    body.Add(Slider("Amount", 0f, 12f, () => StrataSettings.ShapeAmount, v => StrataSettings.ShapeAmount = v, "0.0", "Depth or height in metres; passes of smoothing"));
                    body.Add(SnapRow(shape: true));
                    var row = Row();
                    row.style.marginTop = 6;
                    var apply = Button("Apply", StrataTool.ApplyShape, "Enter");
                    var drop = Button("Drop", () => { StrataTool.Selection = null; SceneView.RepaintAll(); }, "Esc");
                    apply.style.flexGrow = drop.style.flexGrow = 1;
                    row.Add(apply); row.Add(drop);
                    body.Add(row);
                    refreshers.Add(() => { apply.SetEnabled(StrataTool.Selection.HasValue); drop.SetEnabled(StrataTool.Selection.HasValue); });
                    break;
                case StrataBrush.Revert:
                    if (t.Original == null) body.Add(Note("This terrain kept no original."));
                    body.Add(SizeRows());
                    body.Add(Toggle("Heights and walls", null, () => StrataSettings.RevertHeights, v => StrataSettings.RevertHeights = v));
                    body.Add(Toggle("Ground paint", null, () => StrataSettings.RevertPaint, v => StrataSettings.RevertPaint = v));
                    break;
            }
            root.Add(Footer());
            Refresh();
            root.schedule.Execute(KeepInside);
        }

        VisualElement BrushBar()
        {
            var bar = Row();
            bar.style.justifyContent = Justify.SpaceBetween;
            foreach (StrataBrush b in Enum.GetValues(typeof(StrataBrush)))
            {
                if (b == StrataBrush.Sides) continue;
                var btn = new Button(() => { StrataSettings.Brush = b; StrataTool.Selection = null; Build(); SceneView.RepaintAll(); }) { tooltip = Tooltip(b) };
                btn.style.width = 38; btn.style.height = 38;
                btn.style.flexDirection = FlexDirection.Column; btn.style.alignItems = Align.Center; btn.style.justifyContent = Justify.Center;
                btn.style.paddingLeft = btn.style.paddingRight = 0; btn.style.marginLeft = btn.style.marginRight = 1;
                var img = new Image { image = StrataIcons.For(b), scaleMode = ScaleMode.ScaleToFit };
                img.style.width = 16; img.style.height = 16;
                btn.Add(img);
                var lbl = new Label(b.ToString()); lbl.style.fontSize = 8; lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                btn.Add(lbl);
                if (b == StrataSettings.Brush) { btn.style.backgroundColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f); btn.style.borderBottomColor = Accent; btn.style.borderBottomWidth = 2; }
                bar.Add(btn);
            }
            return bar;
        }

        static string Tooltip(StrataBrush b) => b switch
        {
            StrataBrush.Level => "Level: raise or dig cells to a height (walls form where levels meet), paint their top, and style their sides, in any mix.\nShift-click picks · Ctrl reverts · - and = step",
            StrataBrush.Paint => "Paint: ground layers.\nShift-click picks a layer · Ctrl reverts",
            StrataBrush.Sides => "Sides: wall and top-edge style.\nShift-click picks a style · Ctrl reverts",
            StrataBrush.Stairs => "Stairs: drag from the foot to the top.\nCtrl-click removes a flight",
            StrataBrush.Shape => "Shape: drag over cells, then Apply (Enter)",
            _ => "Revert: back to how the terrain was made",
        };

        VisualElement LevelRow()
        {
            var row = Row();
            var label = new Label { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 12, unityTextAlign = TextAnchor.MiddleCenter } };
            var down = Button("−", () => { StrataSettings.Level = StrataSettings.StepLevel(-1); StrataSessions.Raise(); SceneView.RepaintAll(); }, "Level down (-)");
            var up = Button("+", () => { StrataSettings.Level = StrataSettings.StepLevel(1); StrataSessions.Raise(); SceneView.RepaintAll(); }, "Level up (=)");
            down.style.width = up.style.width = 26; down.style.fontSize = up.style.fontSize = 15;
            row.Add(down); row.Add(label); row.Add(up);
            var steps = new[] { 0.25f, 0.5f, 1f, 2f, 4f };
            var stepField = new PopupField<float>(new List<float>(steps), Mathf.Max(0, Array.IndexOf(steps, StrataSettings.Step)), v => $"step {v:0.##} m", v => $"{v:0.##} m") { tooltip = "Level step" };
            stepField.style.width = 74;
            stepField.RegisterValueChangedCallback(e => { StrataSettings.Step = e.newValue; Refresh(); });
            row.Add(stepField);
            refreshers.Add(() =>
            {
                float l = StrataSettings.Level, s = StrataSettings.Step;
                bool whole = Mathf.Abs(l / s - Mathf.Round(l / s)) < 1e-3f;
                label.text = whole ? $"Level {Mathf.Round(l / s)} · {l:0.##} m" : $"{l:0.##} m";
            });
            return row;
        }

        VisualElement ApplyRow()
        {
            var row = Row();
            var parts = new (string name, string tip, Func<bool> get, Action<bool> set)[]
            {
                ("Height", "Raise or dig to the level", () => StrataSettings.ApplyHeight, v => StrataSettings.ApplyHeight = v),
                ("Top", "Paint the top (ground)", () => StrataSettings.ApplyTop, v => StrataSettings.ApplyTop = v),
                ("Sides", "Style the walls and their edge", () => StrataSettings.ApplySides, v => StrataSettings.ApplySides = v),
            };
            foreach (var part in parts)
            {
                var (name, tip, get, set) = part;
                var b = new Button(() =>
                {
                    bool on = get();
                    int count = (StrataSettings.ApplyHeight ? 1 : 0) + (StrataSettings.ApplyTop ? 1 : 0) + (StrataSettings.ApplySides ? 1 : 0);
                    if (on && count == 1) return;
                    set(!on);
                    Build();
                    SceneView.RepaintAll();
                }) { text = name, tooltip = tip };
                b.style.flexGrow = 1; b.style.height = 22; b.style.marginLeft = b.style.marginRight = 1;
                Highlight(b, get());
                row.Add(b);
            }
            return row;
        }

        VisualElement StyleRows(StrataPalette p)
        {
            var wrap = new VisualElement();
            wrap.Add(Chips("Sides", p.walls, () => StrataSettings.Wall, v => StrataSettings.Wall = v, swatches: p.wallColors));
            wrap.Add(Chips("Top edge", p.rims, () => StrataSettings.Rim, v => StrataSettings.Rim = v, swatches: p.rimColors));
            return wrap;
        }

        VisualElement SizeRows()
        {
            var wrap = new VisualElement();
            var row = Row();
            row.style.marginTop = 6;
            var lbl = new Label("Size") { tooltip = "Brush size in cells ([ and ])" };
            lbl.style.width = 50; lbl.style.fontSize = 11;
            var s = new SliderInt(1, 64) { tooltip = lbl.tooltip };
            s.style.flexGrow = 1;
            var field = new IntegerField { tooltip = lbl.tooltip };
            field.style.width = 40;
            s.RegisterValueChangedCallback(e => { StrataSettings.Size = e.newValue; field.SetValueWithoutNotify(StrataSettings.Size); SceneView.RepaintAll(); });
            field.RegisterValueChangedCallback(e => { StrataSettings.Size = e.newValue; s.SetValueWithoutNotify(StrataSettings.Size); SceneView.RepaintAll(); });
            field.RegisterCallback<FocusOutEvent>(_ => field.SetValueWithoutNotify(StrataSettings.Size));
            refreshers.Add(() => { int v = StrataSettings.Size; s.SetValueWithoutNotify(v); if (field.focusController?.focusedElement != field) field.SetValueWithoutNotify(v); });
            row.Add(lbl); row.Add(s); row.Add(field);
            wrap.Add(row);
            wrap.Add(Chips("Brush", new[] { (0, "Square"), (1, "Round") }, () => StrataSettings.Round ? 1 : 0, v => StrataSettings.Round = v == 1));
            wrap.Add(SnapRow(shape: false));
            return wrap;
        }

        VisualElement SnapRow(bool shape)
        {
            var items = shape ? new[] { (0, "Cell"), (4, "Custom") } : new[] { (0, "Cell"), (1, "¼"), (2, "½"), (3, "Block"), (4, "Custom") };
            var tips = shape
                ? new[] { "The selection follows the cursor cell by cell", "The selection snaps to a grid of this many cells" }
                : new[] { "Moves cell by cell", "Moves a quarter of the brush at a time", "Moves half the brush at a time", "Moves a whole brush at a time: squares tile", "Moves on a grid of this many cells" };
            var wrap = Chips("Snap", items, () => shape ? (StrataSettings.Snap == SnapMode.Custom ? 4 : 0) : (int)StrataSettings.Snap, v => StrataSettings.Snap = (SnapMode)v, tips: tips);
            var row = wrap[1];
            var field = new IntegerField { tooltip = "Grid step in cells" };
            field.style.width = 40; field.style.marginLeft = 2;
            field.RegisterValueChangedCallback(e => { StrataSettings.SnapCustom = e.newValue; SceneView.RepaintAll(); });
            field.RegisterCallback<FocusOutEvent>(_ => field.SetValueWithoutNotify(StrataSettings.SnapCustom));
            row.Add(field);
            refreshers.Add(() =>
            {
                field.style.display = StrataSettings.Snap == SnapMode.Custom ? DisplayStyle.Flex : DisplayStyle.None;
                if (field.focusController?.focusedElement != field) field.SetValueWithoutNotify(StrataSettings.SnapCustom);
            });
            return wrap;
        }

        VisualElement Layers(StrataPalette p)
        {
            var wrap = new VisualElement();
            wrap.Add(Caption("Ground"));
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap;
            var cells = new List<VisualElement>();
            for (int k = 0; k < p.layers.Length; k++)
            {
                if (p.layers[k] == null) continue;
                int layer = k;
                var cell = new Button(() => { StrataSettings.Layer = layer; Refresh(); }) { tooltip = p.layers[k] };
                cell.style.width = 46; cell.style.height = 52; cell.style.marginLeft = cell.style.marginRight = 1; cell.style.marginTop = cell.style.marginBottom = 1;
                cell.style.paddingLeft = cell.style.paddingRight = cell.style.paddingTop = 2; cell.style.flexDirection = FlexDirection.Column; cell.style.alignItems = Align.Center;
                var img = new Image { image = k < p.thumbs.Length ? p.thumbs[k] : null, scaleMode = ScaleMode.ScaleAndCrop };
                img.style.width = 36; img.style.height = 36;
                if (img.image == null) img.style.backgroundColor = Color.gray;
                cell.Add(img);
                var name = new Label(p.layers[k]); name.style.fontSize = 8; name.style.unityTextAlign = TextAnchor.MiddleCenter; name.style.overflow = Overflow.Hidden;
                cell.Add(name);
                cell.userData = layer;
                cells.Add(cell);
                grid.Add(cell);
            }
            refreshers.Add(() => { foreach (var c in cells) Highlight(c, (int)c.userData == StrataSettings.Layer); });
            wrap.Add(grid);
            return wrap;
        }

        VisualElement Footer()
        {
            var wrap = new VisualElement();
            wrap.style.marginTop = 8; wrap.style.paddingTop = 4;
            wrap.style.borderTopWidth = 1; wrap.style.borderTopColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            var row = Row();
            var status = new Label { style = { flexGrow = 1, flexShrink = 1, fontSize = 10, unityTextAlign = TextAnchor.MiddleLeft, whiteSpace = WhiteSpace.Normal } };
            var bake = Button("Bake", () => { try { StrataSessions.Notice = StrataSessions.Active?.Bake(); } catch (Exception ex) { StrataSessions.Notice = ex.Message; Debug.LogException(ex); } StrataSessions.Raise(); },
                "Weld, refresh colliders and save. Also happens before Play and when the scene is saved.");
            var prefs = new Button(() => SettingsService.OpenUserPreferences(StrataPreferences.Path)) { tooltip = "Strata preferences" };
            prefs.style.width = 22; prefs.style.height = 20; prefs.style.paddingLeft = prefs.style.paddingRight = 0; prefs.style.alignItems = Align.Center; prefs.style.justifyContent = Justify.Center;
            var gear = new Image { image = StrataIcons.Gear, scaleMode = ScaleMode.ScaleToFit };
            gear.style.width = 14; gear.style.height = 14;
            prefs.Add(gear);
            var mats = new Button(() => StrataWindow.ShowMaterials(Formats.StrataProfiles.Library(StrataSessions.Active))) { tooltip = "Materials: tops, sides, edges and stair types" };
            mats.style.width = 22; mats.style.height = 20; mats.style.paddingLeft = mats.style.paddingRight = 0; mats.style.alignItems = Align.Center; mats.style.justifyContent = Justify.Center;
            var swatch = new Image { image = StrataIcons.Materials, scaleMode = ScaleMode.ScaleToFit };
            swatch.style.width = 14; swatch.style.height = 14;
            mats.Add(swatch);
            row.Add(status); row.Add(bake); row.Add(mats); row.Add(prefs);
            wrap.Add(row);
            refreshers.Add(() =>
            {
                var a = StrataSessions.Active;
                if (a == null) return;
                bool unsaved = a.Unsaved || a.Unbaked;
                status.text = $"{a.Name} · {(unsaved ? "unsaved" : "saved")}" + (string.IsNullOrEmpty(StrataSessions.Notice) ? "" : "\n" + StrataSessions.Notice);
                bake.SetEnabled(unsaved);
            });
            return wrap;
        }

        static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; r.style.marginTop = 2;
            return r;
        }

        static Label Caption(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10; l.style.color = new Color(0.6f, 0.6f, 0.6f); l.style.marginTop = 4; l.style.marginLeft = 2;
            return l;
        }

        static Label Note(string text)
        {
            var l = new Label(text);
            l.style.whiteSpace = WhiteSpace.Normal; l.style.fontSize = 11; l.style.marginTop = 4; l.style.marginBottom = 4;
            return l;
        }

        static Button Button(string text, Action click, string tip = null)
        {
            var b = new Button(click) { text = text, tooltip = tip };
            b.style.flexGrow = 0;
            return b;
        }

        VisualElement Chips(string caption, (int index, string name)[] items, Func<int> get, Action<int> set, Color[] swatches = null, string[] tips = null, bool rebuild = false)
        {
            var wrap = new VisualElement();
            wrap.Add(Caption(caption));
            var row = Row();
            row.style.flexWrap = Wrap.Wrap;
            var buttons = new List<(Button b, int index)>();
            for (int k = 0; k < items.Length; k++)
            {
                var (index, name) = items[k];
                int v = index;
                var b = new Button(() => { set(v); if (rebuild) Build(); else Refresh(); SceneView.RepaintAll(); }) { text = name, tooltip = tips != null && k < tips.Length ? tips[k] : null };
                b.style.marginLeft = b.style.marginRight = 1; b.style.paddingLeft = b.style.paddingRight = 6; b.style.height = 22;
                if (swatches != null && v >= 0 && v < swatches.Length)
                {
                    var dot = new VisualElement();
                    dot.style.width = 8; dot.style.height = 8; dot.style.marginRight = 4;
                    dot.style.backgroundColor = swatches[v];
                    dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 2;
                    b.style.flexDirection = FlexDirection.Row; b.style.alignItems = Align.Center;
                    b.text = "";
                    b.Add(dot); b.Add(new Label(name));
                }
                buttons.Add((b, v));
                row.Add(b);
            }
            refreshers.Add(() => { int cur = get(); foreach (var (b, v) in buttons) Highlight(b, v == cur); });
            wrap.Add(row);
            return wrap;
        }

        static void Highlight(VisualElement e, bool on)
        {
            e.style.backgroundColor = on ? new Color(Accent.r, Accent.g, Accent.b, 0.38f) : StyleKeyword.Null;
            var c = on ? Accent : new Color(0, 0, 0, 0);
            e.style.borderBottomColor = e.style.borderTopColor = e.style.borderLeftColor = e.style.borderRightColor = on ? c : StyleKeyword.Null;
        }

        VisualElement Toggle(string text, string tip, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(text) { tooltip = tip };
            t.style.marginTop = 4;
            t.RegisterValueChangedCallback(e => { set(e.newValue); SceneView.RepaintAll(); });
            refreshers.Add(() => t.SetValueWithoutNotify(get()));
            return t;
        }

        VisualElement Slider(string text, float min, float max, Func<float> get, Action<float> set, string format, string tip = null)
        {
            var row = Row();
            var lbl = new Label(text) { tooltip = tip };
            lbl.style.width = 50; lbl.style.fontSize = 11;
            var s = new Slider(min, max) { tooltip = tip };
            s.style.flexGrow = 1;
            var val = new Label(); val.style.width = 40; val.style.unityTextAlign = TextAnchor.MiddleRight; val.style.fontSize = 11;
            s.RegisterValueChangedCallback(e => { set(e.newValue); val.text = get().ToString(format); SceneView.RepaintAll(); });
            refreshers.Add(() => { float v = get(); s.SetValueWithoutNotify(v); val.text = v.ToString(format); });
            row.Add(lbl); row.Add(s); row.Add(val);
            return row;
        }
    }
}
