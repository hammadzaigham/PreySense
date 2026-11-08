using System.ComponentModel;
using System.Threading;
using PreySense.Frontend.Views;

namespace PreySense.Tests;

[TestClass]
public class RequiredCalibrationTests
{

    private static bool WouldCancelClose(CustomModalDialog dialog)
    {
        var args = new CancelEventArgs();
        typeof(CustomModalDialog).GetMethod("OnClosing",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(dialog, new object[] { args });
        return args.Cancel;
    }

    [TestMethod]
    public void ProgressDialogCannotCloseUntilCaptureFinishes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new CustomModalDialog { AllowClose = false };
                Assert.IsTrue(WouldCancelClose(dialog));
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(CustomModalDialog).GetMethod("CancelButton_Click", flags)!
                    .Invoke(dialog, new object[] { dialog, new System.Windows.RoutedEventArgs() });
                Assert.IsFalse((bool?)typeof(CustomModalDialog).GetField("_result", flags)!.GetValue(dialog));
                typeof(CustomModalDialog).GetMethod("CloseButton_Click", flags)!
                    .Invoke(dialog, new object[] { dialog, new System.Windows.RoutedEventArgs() });
                Assert.IsNull(typeof(CustomModalDialog).GetField("_result", flags)!.GetValue(dialog));
                dialog.AllowClose = true;
                Assert.IsFalse(WouldCancelClose(dialog));
                dialog.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure != null) throw new AssertFailedException($"Calibration dialog close guard failed: {failure}", failure);
    }
}
