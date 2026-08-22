================================================================================
                         UI JUICE - FREE VERSION v1.5.0
              Professional Button & Panel Animations for Unity
================================================================================

🎉 Thank you for downloading UI Juice Free!

This free version gives you professional UI animations to add "juice" to your
Unity projects with zero coding required.

⭐ Want MORE? Upgrade to UI Juice Pro for 100+ animation combinations!
    https://assetstore.unity.com/packages/slug/328841

================================================================================
                          🚀 QUICK START (3 Steps)
================================================================================

STEP 1: INSTALL DOTWEEN (Required - 2 minutes)
-----------------------------------------------
1. Open Unity Asset Store (Window → Asset Store)
2. Search for "DOTween" (by Demigiant)
3. Download and Import the FREE version
4. Click "Setup DOTween..." when the panel appears

⚠️ IMPORTANT: UI Juice requires DOTween to function. It's free and easy!


STEP 2: EXPLORE DEMO SCENE (2 minutes)
---------------------------------------
Open the demo scene to see animations in action:

📁 Demo/ButtonsDemo_Free - Interactive showcase of all effects

Just press Play and hover/click the buttons!

📁 Demo/Panel Animation_DEMO - All Panel Animation showcase

Press Play Select Animation Setting and see the magic.


STEP 3: ADD TO YOUR UI (1 minute)
----------------------------------
OPTION A - Buttons:
  1. Select your Button GameObject
  2. Set Button → Transition to "None" (CRITICAL!)
  3. Add Component → ButtonAnimator_Free
  4. Choose hover and click effects
  5. Press Play!

OPTION B - Panels:
  1. Select your Panel GameObject
  2. Add Component → PanelAnimator_Free
  3. Choose in/out animations
  4. Press Play!

================================================================================
                         ✨ WHAT'S INCLUDED (Free)
================================================================================

🔘 BUTTON ANIMATOR (LIMITED)
-----------------------------
5 Hover Effects:
  • Scale - Universal smooth grow ⭐ Most popular
  • BounceScale - Playful bouncy feel
  • ColorTint - Subtle color change
  • Glow - Modern outline effect
  • Pulse - Breathing animation

5 Click Effects:
  • Punch - Universal quick shrink ⭐ Most popular
  • Squeeze - Squash and stretch
  • Flash - Color flash feedback
  • Shake - Position shake (great for errors!)
  • Jello - Jiggly wobble

📱 PANEL ANIMATOR (LIMITED)
---------------------------
4 Animation Types:
  • Fade - Simple opacity transition ⭐ Most popular
  • Slide - Slides from 4 directions
  • Scale - Grows/shrinks smoothly
  • Pop - Bouncy scale effect

💥 STANDALONE EFFECTS
---------------------
Animate_Shake_Free:
  • Position shake (full shake)
  • Horizontal shake (X-axis only)
  • Rotation wobble (subtle rotation)

Animate_Pulse_Free:
  • Scale pulse (classic breathing)
  • Breathe pulse (slower, gentler)

📺 DEMO SCENE
-------------
• ButtonsDemo_Free - Shows all 10 button effects + panel animations

================================================================================
                    🔓 UPGRADE TO UI JUICE PRO
================================================================================

Love the free version? UI Juice Pro gives you 10X MORE:

📊 MORE EFFECTS:
  ✓ Button: 40+ combinations (vs 10 in free)
  ✓ Panel: 12 animation types (vs 4 in free)
  ✓ Shake: 8 types (vs 3 in free)
  ✓ Pulse: 8 types (vs 2 in free)

🎨 MORE COMPONENTS (7 Additional):
  ✓ Input Field Animator - Floating labels, error/success states
  ✓ Animated Toggle - 8 animation styles, 5 feedback types
  ✓ Animated Slider - Health bars with 3 damage effects
  ✓ Animated Dropdown - 10 template + 10 item animations
  ✓ Animated Scroll View - 14 entrance animations
  ✓ Animated Tab Menu - Complete tab system with 7 indicator styles
  ✓ Enhanced Shake & Pulse - All 8 types each

📚 MORE CONTENT:
  ✓ 3 Additional demo scenes (Tab Menu, Scroll View, Main Menu)
  ✓ 40+ page comprehensive documentation
  ✓ Quick reference card
  ✓ 20+ ready-to-use prefabs
  ✓ Migration guide
  ✓ Video tutorials

💎 MORE SUPPORT:
  ✓ Priority email support (24-48hr vs 48-72hr)
  ✓ Regular updates with new features
  ✓ Access to future components
  ✓ Professional production-ready quality

💰 SPECIAL OFFER:
  Use code "FREEJUICE10" for 10% off UI Juice Pro!
  (Limited time offer)

🔗 UPGRADE NOW:
  https://assetstore.unity.com/packages/[YOUR_LINK_HERE]

================================================================================
                         🎯 WHAT YOU CAN DO
================================================================================

✅ GREAT FOR:
-------------
- Main menu buttons
- Basic panel show/hide
- UI navigation
- Confirmation dialogs
- Simple games
- Prototyping
- Learning UI animation
- Testing before buying Pro

❌ YOU'LL NEED PRO FOR:
-----------------------
- Login/signup forms (Input Fields)
- Settings panels (Toggles)
- Health bars (Sliders)
- Inventory systems (Scroll Views)
- Tab navigation (Tab Menu)
- Dropdown menus
- Complex UI systems
- Production games

================================================================================
                         💡 IMPORTANT TIPS
================================================================================

✅ DO:
------
- Set Button → Transition to "None" before adding ButtonAnimator_Free
- Use the demo scene to explore all effects
- Start with Scale hover + Punch click (universal favorites)
- Use PanelAnimator for smooth panel transitions
- Cache component references in your scripts

❌ DON'T:
---------
- Forget to install DOTween first!
- Skip the demo scene
- Use Float effects with Layout Groups (Pro only feature)
- Stack multiple effects on same object

================================================================================
                         🐛 COMMON ISSUES & FIXES
================================================================================

ISSUE: "DOTween could not be found"
FIX: Import DOTween from Asset Store (it's free!)

ISSUE: Buttons don't animate
FIX: Set Button → Transition to "None" ⚠️

ISSUE: Want more effects
FIX: Upgrade to UI Juice Pro! ✨

ISSUE: Need input fields, toggles, sliders
FIX: These are only in UI Juice Pro

ISSUE: Animations feel slow
FIX: Reduce duration values in Inspector (0.15-0.2s is snappy)

For more solutions, check the demo scene tooltips!


================================================================================
                         💻 CODE EXAMPLES
================================================================================

BUTTON ANIMATOR:
----------------
// No code needed! Configure in Inspector.
// But if you want to trigger from code:

using SpankyBoy.DOTweenGUI.Free;

public class MyUI : MonoBehaviour
{
    public ButtonAnimator_Free playButton;
    
    // Buttons work automatically!
    // Just add the component and configure.
}


PANEL ANIMATOR:
---------------
using SpankyBoy.DOTweenGUI.Free;

public class MenuManager : MonoBehaviour
{
    public PanelAnimator_Free settingsPanel;
    
    public void OpenSettings()
    {
        settingsPanel.Show(); // or .AnimateIn()
    }
    
    public void CloseSettings()
    {
        settingsPanel.Hide(); // or .AnimateOut()
    }
}


SHAKE EFFECT:
-------------
using SpankyBoy.DOTweenGUI.Free;

public class FormValidator : MonoBehaviour
{
    public Animate_Shake_Free passwordShake;
    
    void OnPasswordError()
    {
        passwordShake.Animate(); // Shake to show error!
    }
}


PULSE EFFECT:
-------------
using SpankyBoy.DOTweenGUI.Free;

public class NotificationBadge : MonoBehaviour
{
    public Animate_Pulse_Free badgePulse;
    
    void OnNewNotification()
    {
        badgePulse.StartPulse(); // Draw attention!
    }
    
    void OnNotificationRead()
    {
        badgePulse.StopPulse();
    }
}

See Demo scene for more examples!

================================================================================
                         🎨 BEST EFFECT COMBINATIONS
================================================================================

UNIVERSAL (Works Everywhere):
  Hover: Scale
  Click: Punch
  → Professional and responsive

PLAYFUL (Games):
  Hover: BounceScale
  Click: Jello
  → Fun and energetic

MODERN (Apps):
  Hover: Glow
  Click: Squeeze
  → Clean and contemporary

SUBTLE (Professional):
  Hover: ColorTint
  Click: Flash
  → Understated elegance

ATTENTION-GRABBING:
  Hover: Pulse
  Click: Shake
  → Impossible to miss

PANEL TRANSITIONS:
  Settings: Slide In/Out
  Dialogs: Pop In, Fade Out
  Overlays: Fade In/Out
  Menus: Slide In, Scale Out

================================================================================
                         📞 SUPPORT & FEEDBACK
================================================================================

FREE VERSION SUPPORT:
---------------------
Email: fluxentinteractive@gmail.com
Subject: "UI Juice Free - [Your Issue]"
Response: 48-72 hours

Before emailing:
1. Check this README
2. Review demo scene
3. Verify DOTween is installed
4. Check Button → Transition is "None"

When reporting issues, include:
- Unity version
- What you're trying to do
- Screenshot of Inspector
- Error messages (if any)

FEATURE REQUESTS:
-----------------
Have ideas for the free version? Let us know!
We actively shape updates based on feedback.

(Most requested features are added to Pro version first)

UPGRADE QUESTIONS:
------------------
Questions about UI Juice Pro? Email us!
We're happy to help you decide if Pro is right for you.

================================================================================
                         ⭐ ENJOYING IT?
================================================================================

If UI Juice Free helped your project:

1. ⭐ Rate it 5 stars on the Asset Store
2. 📝 Leave a review (helps other developers!)
3. 🔄 Share with your gamedev friends
4. 💰 Consider upgrading to UI Juice Pro
5. 📧 Send us screenshots of your UI!

Every review helps us create better tools!

================================================================================
                         🔓 FREE VS PRO COMPARISON
================================================================================

                        FREE          UI JUICE PRO
                        ----          ------------
Button Hover Effects     5               14
Button Click Effects     5               14
Button Features       Basic          Full (Toggle, Groups, etc.)
Panel Animations         4               12
Shake Types              3               8
Pulse Types              2               8
Input Fields            ❌              ✅
Toggles                 ❌              ✅
Sliders                 ❌              ✅
Dropdowns               ❌              ✅
Scroll Views            ❌              ✅
Tab Menus               ❌              ✅
Demo Scenes              1               4
Documentation        5 pages         40+ pages
Prefabs                 ❌              20+
Support              Email           Priority Email
Updates              Major           All Updates
Price                FREE            $19.99

================================================================================
                         🚀 READY TO UPGRADE?
================================================================================

The free version is great for learning and small projects.

But if you're serious about UI polish, UI Juice Pro gives you:
• 10X more animations
• 7 additional components
• Professional documentation
• Production-ready prefabs
• Priority support
• All future updates

🔗 UPGRADE TO UI JUICE PRO:
https://assetstore.unity.com/packages/[YOUR_LINK_HERE]

Use code "FREEJUICE10" for 10% off! (Limited time)

================================================================================
                         🎉 THANK YOU!
================================================================================

Thank you for trying UI Juice Free!

We hope these animations make your UI feel amazing. If you want to take
your UI to the next level, check out UI Juice Pro.

Here's your quick roadmap:
1. ✅ Install DOTween
2. ✅ Open ButtonsDemo_Free scene
3. ✅ Try effects on your buttons
4. ✅ Explore PanelAnimator
5. 🚀 Upgrade to Pro when ready!

Happy developing! 🎮

================================================================================

UI Juice Free v1.5.0
Made with ❤️ by Fluxent Interactive © 2025

Full Version: UI Juice Pro
Available on Unity Asset Store
https://assetstore.unity.com/packages/tools/gui/ui-animation-kit-for-dotween-328841

Free Version - No Commercial Restrictions
Use in any project. No attribution required.

================================================================================