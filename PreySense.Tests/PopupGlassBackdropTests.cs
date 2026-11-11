using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class PopupGlassBackdropTests
{
    [TestMethod]
    public void ExistingClosedPopup_UsesTheNewThemeBeforeItsFirstOpening()
    {
        if (!ThemeManager.IsGlassSupported) Assert.Inconclusive("Desktop Acrylic requires Windows 11 22621 or newer.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previous = ThemeManager.CurrentTheme;
            var shadow = new DropShadowEffect();
            var host = new Border { Effect = shadow };
            var popup = new Popup { Child = host };
            try
            {
                ThemeManager.Apply(ApplicationTheme.Dark, persist: false);
                PopupGlassBackdrop.SetEnable(popup, true);
                Assert.IsTrue(popup.AllowsTransparency);
                Assert.AreSame(shadow, host.Effect);
                ThemeManager.Apply(ApplicationTheme.Glass, persist: false);
                PopupGlassBackdrop.RefreshTheme();
                Assert.IsFalse(popup.AllowsTransparency, "The first Glass opening must create a non-layered HWND.");
                Assert.IsNull(host.Effect);
                ThemeManager.Apply(ApplicationTheme.Dark, persist: false);
                PopupGlassBackdrop.RefreshTheme();
                Assert.IsTrue(popup.AllowsTransparency);
                Assert.AreSame(shadow, host.Effect);
                PopupGlassBackdrop.SetEnable(popup, false);
                ThemeManager.Apply(ApplicationTheme.Glass, persist: false);
                PopupGlassBackdrop.RefreshTheme();
                Assert.IsTrue(popup.AllowsTransparency, "Disabled attached behavior must stop changing the popup.");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                PopupGlassBackdrop.SetEnable(popup, false);
                ThemeManager.Apply(previous, persist: false);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
