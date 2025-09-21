using ExpandedStomach;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedStomach.Hud
{
    public class HudESBar : HudElement
    {
        //Linear bar UI Element
        private GuiElementStatbar _esBar;

        //Client-side states for smooth updates
        private float _displayedHunger;
        private float _displayedMaxHunger;
        private bool _hideESBarOnEmpty;
        private bool _showBar;
        //auto-hide controls
        private bool _autoHideESBar;
        private bool _hudOpen;
        private double _emptyElapsed;
        //game tick listeners
        private long _serverSyncListenerID;
        private long _visualUpdateListenerID;

        ConfigServer config = ExpandedStomachModSystem.sConfig;

        public HudESBar(ICoreClientAPI capi) : base(capi)
        {
            _autoHideESBar = config.audoHideHungerBar;
            _hideESBarOnEmpty = config.audoHideHungerBar;
            _showBar = config.bar;

            ComposeBarGUI();
            _hudOpen = true;

            _serverSyncListenerID = capi.Event.RegisterGameTickListener(OnServerSync, 50); //update 20 times a second
            _visualUpdateListenerID = capi.Event.RegisterGameTickListener(OnVisualUpdate, 50); //update 20 times a second
        }

        public override void OnOwnPlayerDataReceived()
        {
            base.OnOwnPlayerDataReceived();
            ComposeBarGUI(); // call this to make sure bar is fully updated;
        }

        private void ComposeBarGUI()
        {
            if (!_showBar) return;
            const float ESBarParentWidth = 850f;
            const float ESBarWidth = ESBarParentWidth * 0.41f;

            double yOffset = 96; //establish initial position of the bar
            yOffset += ExpandedStomachModSystem.IsHODLoaded ? 22 : 0; //add 22 if HOD is loaded
            yOffset += ExpandedStomachModSystem.IsVigorLoaded ? 22 : 0; //add 22 if vigor is also loaded
            yOffset += config.barVerticalOffset; //add vertical offset from config in case of other mod conflicts
            double esBarHeight = 10;

            var esBarBounds = new ElementBounds()
            {
                Alignment = EnumDialogArea.CenterBottom,
                BothSizing = ElementSizing.Fixed,
                fixedWidth = ESBarParentWidth,
                fixedHeight = 10
            };
            bool isRightSide = true;
            double alignmentOffset = isRightSide ? -2.0 : 1.0;

            //create bar bounds WITHOUT horizontal offset (will be added to parent container)
            var hungerBarBounds = ElementStdBounds.Statbar(
                isRightSide ? EnumDialogArea.RightMiddle : EnumDialogArea.LeftMiddle,
                ESBarWidth
            ).WithFixedHeight(10);

            //es bar color -- mimic hunger bar from base game
            var esBarColor = new double[] { 0.482, 0.521, 0.211, 1 };

            var barParentBounds = esBarBounds.FlatCopy()
                .FixedGrow(0.0, esBarHeight)
                .WithFixedOffset(0, -yOffset)
                .WithFixedAlignmentOffset(alignmentOffset, 0);

            var composer = capi.Gui.CreateCompo("esbarhud", barParentBounds);

            composer.BeginChildElements(esBarBounds);

            _esBar = new GuiElementStatbar(composer.Api, hungerBarBounds, esBarColor, isRightSide, false);
            composer.AddInteractiveElement(_esBar, "eshungerbar");

            composer.EndChildElements();
            Composers["esbarhud"] = composer.Compose();

            TryOpen();
        }

        private void OnServerSync(float dt)
        {
            var player = capi?.World?.Player;
            if (player?.Entity == null) return;

            var expandedStomachTree = player.Entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if (expandedStomachTree == null) return;

            //get values for meter
            var serverStomachMeter = expandedStomachTree.GetFloat("expandedStomachMeter");
            var serverMaxStomachMeter = (float)expandedStomachTree.GetInt("stomachSize");

            //store values for use in update
            _displayedHunger = serverStomachMeter;
            _displayedMaxHunger = serverMaxStomachMeter;
        }

        private void OnVisualUpdate(float dt)
        {
            //try to update config values from internal config settings
            var config = ExpandedStomachModSystem.coreapi?.World?.Config;
            if (config == null) return;
            _autoHideESBar = config.GetBool("ExpandedStomach.audoHideHungerBar");
            _hideESBarOnEmpty = config.GetBool("ExpandedStomach.audoHideHungerBar");
            _showBar = config.GetBool("ExpandedStomach.bar");
            if (!_showBar)
            {
                base.TryClose();
                _hudOpen = false;
                return;
            }
            HandleAutoHide(dt);
            UpdateHungerDisplay();
        }

        private void HandleAutoHide(float dt)
        {
            if(!_autoHideESBar) return;
            float max = GameMath.Clamp(_displayedMaxHunger, 500f, 5500f);
            float hunger = _displayedHunger;
            if (hunger > max) hunger = max;
            if (hunger < 0f) hunger = 0f;
            bool isEmpty = hunger < 1f;

            if (isEmpty)
            {
                _emptyElapsed += dt;
                if (_emptyElapsed > 1.0 && _hudOpen)
                {
                    base.TryClose();
                    _hudOpen = false;
                }
            }
            else
            {
                _emptyElapsed = 0.0;
                if (!_hudOpen)
                {
                    TryOpen();
                    _hudOpen = true;
                }
            }
        }

        private void UpdateHungerDisplay()
        {
            if (_esBar == null) return;

            float max = GameMath.Clamp(_displayedMaxHunger, 500f, 5500f);
            float hunger = _displayedHunger;
            if(hunger > max) hunger = max;
            if(hunger < 0f) hunger = 0f;
            if(max - hunger < 1f) hunger = max;

            _esBar.SetValues(hunger, 0, max);
            _esBar.ShouldFlash = false;
            _esBar.SetLineInterval(100);
        }

        public override void Dispose()
        {
            base.Dispose();
            capi.Event.UnregisterGameTickListener(_serverSyncListenerID);
            capi.Event.UnregisterGameTickListener(_visualUpdateListenerID);
        }

        public override bool TryClose()
        {
            return base.TryClose();
        }
        
        public override bool ShouldReceiveKeyboardEvents() => false;

        public override bool Focusable => false;
    }
}
