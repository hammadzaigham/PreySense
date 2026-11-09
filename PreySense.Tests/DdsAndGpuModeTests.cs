using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Services;
using PreySense.Helpers;
using PreySense.Gpu;

namespace PreySense.Tests
{
    [TestClass]
    public class DdsAndGpuModeTests
    {
        [TestMethod]
        public void Test_BiosMux_Payload_Matches_PredatorSense_GpuMode()
        {
            Assert.AreEqual(0x102ul, WmiController.GpuMuxPayload(WmiController.GpuMuxOptimus));
            Assert.AreEqual(0x202ul, WmiController.GpuMuxPayload(WmiController.GpuMuxDiscrete));
            Assert.AreEqual(0x302ul, WmiController.GpuMuxPayload(WmiController.GpuMuxAuto));
            Assert.AreEqual(WmiController.GpuMuxDiscrete, WmiController.GpuMuxFromFirmware(0x200));
            Assert.AreEqual(WmiController.GpuMuxAuto, WmiController.GpuMuxFromFirmware(0x300));
            Assert.AreEqual(WmiController.GpuMuxOptimus, WmiController.GpuMuxFromFirmware(0x100));
        }

        [TestMethod]
        public void Test_MockHardwareService_DdsMethods()
        {
            IHardwareService service = new MockHardwareService();

            Assert.IsTrue(service.IsDdsAvailable);
            Assert.IsTrue(service.IsDdsSupported());
            Assert.IsFalse(service.HasDdsBlockers());

            var initialStatus = service.GetDdsStatus();
            Assert.IsNotNull(initialStatus);
            Assert.IsTrue(initialStatus.IsSupported);

            // Switch to dGPU
            var (ok1, msg1) = service.SetDdsMode(DdsDisplayMode.NvidiaGpuOnly);
            Assert.IsTrue(ok1);
            var statusDgpu = service.GetDdsStatus();
            Assert.IsNotNull(statusDgpu);
            Assert.IsFalse(statusDgpu.IsAutomatic);
            Assert.AreEqual(DdsActiveMux.dGPU, statusDgpu.ActiveMux);

            // Switch to Auto
            var (ok2, msg2) = service.SetDdsMode(DdsDisplayMode.Automatic);
            Assert.IsTrue(ok2);
            var statusAuto = service.GetDdsStatus();
            Assert.IsNotNull(statusAuto);
            Assert.IsTrue(statusAuto.IsAutomatic);
            Assert.AreEqual(DdsActiveMux.Optimus, statusAuto.ActiveMux);

            // Switch to Optimus
            var (ok3, msg3) = service.SetDdsMode(DdsDisplayMode.Optimus);
            Assert.IsTrue(ok3);
            var statusOptimus = service.GetDdsStatus();
            Assert.IsNotNull(statusOptimus);
            Assert.IsFalse(statusOptimus.IsAutomatic);
            Assert.AreEqual(DdsActiveMux.Optimus, statusOptimus.ActiveMux);
        }

        [TestMethod]
        public void Test_NvidiaDdsService_Singleton_Safety()
        {
            var instance = NvidiaDdsService.Instance;
            Assert.IsNotNull(instance);

            // Querying availability should never crash even if running in a non-NVIDIA or CI test environment
            bool isAvailable = instance.IsAvailable;
            if (isAvailable)
            {
                var status = instance.GetStatus();
                Assert.IsNotNull(status);
                // In live environments, verify status object was populated without throwing
                Assert.IsTrue(status.ActiveMux is DdsActiveMux.Unknown or DdsActiveMux.Optimus or DdsActiveMux.dGPU);
            }
        }

        [TestMethod]
        public async System.Threading.Tasks.Task Test_MainViewModel_GpuMuxMode_Switching_And_Notifications()
        {
            var hw = new MockHardwareService();
            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);

            // Initial state: set to Automatic (1) to ensure deterministic test baseline regardless of host user registry
            await vm.SetGpuMuxModeAsync(1);
            Assert.AreEqual("Automatic", vm.GpuMuxModeName);
            Assert.IsTrue(vm.IsGpuAutoActive);
            Assert.IsFalse(vm.IsGpuOptimusActive);
            Assert.IsFalse(vm.IsGpuNvidiaOnlyActive);

            // Track property changed events
            var changedProps = new System.Collections.Generic.List<string>();
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) changedProps.Add(e.PropertyName);
            };

            // Switch to Optimus (0)
            await vm.SetGpuMuxModeAsync(0);
            Assert.AreEqual(0, vm.ActiveGpuMuxMode);
            Assert.AreEqual("Optimus", vm.GpuMuxModeName);
            Assert.IsTrue(vm.IsGpuOptimusActive);
            Assert.IsFalse(vm.IsGpuAutoActive);
            Assert.IsFalse(vm.IsGpuNvidiaOnlyActive);
            CollectionAssert.Contains(changedProps, nameof(vm.IsGpuOptimusActive));

            changedProps.Clear();

            // Switch to Auto (1)
            await vm.SetGpuMuxModeAsync("1");
            Assert.AreEqual(1, vm.ActiveGpuMuxMode);
            Assert.AreEqual("Automatic", vm.GpuMuxModeName);
            Assert.IsTrue(vm.IsGpuAutoActive);
            Assert.IsFalse(vm.IsGpuOptimusActive);
            Assert.IsFalse(vm.IsGpuNvidiaOnlyActive);
            CollectionAssert.Contains(changedProps, nameof(vm.IsGpuAutoActive));

            changedProps.Clear();

            // Switch to NVIDIA GPU Only (2)
            await vm.SetGpuMuxModeAsync(2);
            Assert.AreEqual(2, vm.ActiveGpuMuxMode);
            Assert.AreEqual("NVIDIA GPU Only", vm.GpuMuxModeName);
            Assert.IsTrue(vm.IsGpuNvidiaOnlyActive);
            Assert.IsFalse(vm.IsGpuAutoActive);
            Assert.IsFalse(vm.IsGpuOptimusActive);
            CollectionAssert.Contains(changedProps, nameof(vm.IsGpuNvidiaOnlyActive));

            // CRITICAL: Advanced Optimus is a Windows runtime feature!
            // Switching between Auto, Optimus, and NVIDIA GPU Only must NEVER invoke the BIOS ACPI MUX.
            Assert.AreEqual(0, hw.SetGpuMuxModeCallCount, "Advanced Optimus must NEVER touch BIOS MUX via SetGpuMuxMode.");
        }

        [TestMethod]
        public void Test_GpuMux_DoesNotFallBackToAcerWmi()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(root);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PreySense.slnx")))
                dir = dir.Parent;
            Assert.IsNotNull(dir);
            string gpu = File.ReadAllText(Path.Combine(dir!.FullName, "PreySense.Frontend", "ViewModels", "MainViewModel.Gpu.cs"));
            Assert.Contains("_hw.SetDdsMode(targetDdsMode)", gpu);
            Assert.DoesNotContain("MapUiGpuModeToMuxMode", gpu);
            Assert.Contains("NVIDIA Advanced Optimus is not available", gpu);
        }

        [TestMethod]
        public void Test_CustomModalDialog_Initialization_And_Theme()
        {
            Exception? failure = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var dialog = new PreySense.Frontend.Views.CustomModalDialog
                    {
                        Title = "PreySense • Hardware Discrete GPU",
                        HeaderSubtitle = " • Hardware Display MUX",
                        Message = "Would you like to switch to dGPU Only mode and restart now?",
                        ConfirmText = "Apply & Restart Now"
                    };
                    // Standalone test windows have no App resources. Apply the real shared
                    // style instead of asserting framework defaults or duplicating its setters.
                    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
                    var app = System.Xml.Linq.XDocument.Load(Path.Combine(root, "PreySense.Frontend", "App.xaml"));
                    System.Xml.Linq.XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                    var styleElement = new System.Xml.Linq.XElement(app.Descendants(presentation + "Style")
                        .Single(element => (string?)element.Attribute(xaml + "Key") == "AppWindowRenderingStyle"));
                    styleElement.Attribute(xaml + "Key")!.Remove();
                    styleElement.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", xaml.NamespaceName);
                    styleElement.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "services", "clr-namespace:PreySense.Frontend.Services;assembly=PreySense");
                    dialog.Style = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse(styleElement.ToString());
                    Assert.AreEqual("PreySense • Hardware Discrete GPU", dialog.Title);
                    Assert.AreEqual(" • Hardware Display MUX", dialog.HeaderSubtitle);
                    Assert.AreEqual("Apply & Restart Now", dialog.ConfirmText);
                    Assert.IsFalse(dialog.ShowInTaskbar);
                    Assert.AreEqual(System.Windows.WindowStyle.None, dialog.WindowStyle);
                    Assert.IsFalse(dialog.AllowsTransparency);
                    dialog.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        [TestMethod]
        public void Test_Startup_GpuMode_DoesNotDefaultTo_BiosDiscrete()
        {
            var hw = new MockHardwareService();
            // Even if the legacy Acer ACPI method returns 2 (GpuMuxDiscrete),
            // the system is not in BIOS Discrete mode unless the iGPU is physically absent.
            hw.LastMuxModeSet = 2;
            hw.IsBiosDiscreteGpuActive = false;

            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);

            Assert.IsFalse(vm.IsBiosDiscreteGpuActive, "IsBiosDiscreteGpuActive must not be true when iGPU is present.");
            Assert.IsTrue(vm.IsGpuAutoActive, "Automatic mode should be active on startup when DDS reports Automatic.");
        }

        [TestMethod]
        public void Test_Startup_GpuMode_TrueBiosDiscrete_When_HardwareReportsDiscrete()
        {
            var hw = new MockHardwareService();
            hw.IsBiosDiscreteGpuActive = true;

            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);

            Assert.IsTrue(vm.IsBiosDiscreteGpuActive, "IsBiosDiscreteGpuActive must be true when hardware reports no iGPU.");
            Assert.IsFalse(vm.IsGpuAutoActive);
            Assert.IsFalse(vm.IsGpuOptimusActive);
            Assert.IsFalse(vm.IsGpuNvidiaOnlyActive);
            Assert.IsFalse(vm.ShowDdsGpuTiles);
            Assert.AreEqual(2, vm.GpuModeTileColumns);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task Test_BiosDiscrete_OtherModes_DoNotUseDds()
        {
            var hw = new MockHardwareService { IsBiosDiscreteGpuActive = true };
            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);

            await vm.SetGpuMuxModeAsync(1);
            await vm.SetGpuMuxModeAsync(0);
            await vm.SetGpuMuxModeAsync(2);

            Assert.AreEqual(0, hw.SetGpuMuxModeCallCount);
            Assert.IsTrue(vm.IsBiosDiscreteGpuActive);
            Assert.IsTrue(hw.GetDdsStatus()!.IsAutomatic);
        }
    }
}
