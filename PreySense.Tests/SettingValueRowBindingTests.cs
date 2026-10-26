using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using PreySense.Frontend.Controls;

namespace PreySense.Tests;

[TestClass]
public class SettingValueRowBindingTests
{
    [TestMethod]
    public void EditingAndClampingPreserveTwoWayProfileBinding()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new Slider { Minimum = 0, Maximum = 999, Value = 40 };
                var row = new SettingValueRow { Minimum = 0, Maximum = 255 };
                BindingOperations.SetBinding(row, SettingValueRow.ValueProperty,
                    new Binding(nameof(Slider.Value)) { Source = source, Mode = BindingMode.TwoWay });
                var text = (TextBox)row.FindName("InnerValueBox");
                text.Text = "60";
                row.CommitText();
                Assert.AreEqual(60d, source.Value);
                Assert.IsTrue(BindingOperations.IsDataBound(row, SettingValueRow.ValueProperty));
                source.Value = 25;
                Assert.AreEqual(25d, row.Value);
                text.Text = "999";
                row.CommitText();
                Assert.AreEqual(255d, source.Value);
                Assert.IsTrue(BindingOperations.IsDataBound(row, SettingValueRow.ValueProperty));
                source.Value = 40;
                Assert.AreEqual(40d, row.Value);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure != null) throw new AssertFailedException("Number box binding failed.", failure);
    }
}
