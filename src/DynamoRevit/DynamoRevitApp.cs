using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Dynamo.Applications.Properties;
using RevitServices.Elements;
using RevitServices.Persistence;
using RevitServices.Transactions;
using MessageBox = System.Windows.Forms.MessageBox;
using Dynamo.Models;
using RevitServices.EventHandler;

namespace Dynamo.Applications
{
    

    [Transaction(Autodesk.Revit.Attributes.TransactionMode.Automatic),
     Regeneration(RegenerationOption.Manual)]
    public class DynamoRevitApp : IExternalApplication
    {
        private static readonly string assemblyName = Assembly.GetExecutingAssembly().Location;
        private static ResourceManager res;
        public static ControlledApplication ControlledApplication;
        public static UIControlledApplication UIControlledApplication;
        public static List<IUpdater> Updaters = new List<IUpdater>();
        internal static PushButton DynamoButton;
        private static readonly Queue<Action> idleActionQueue = new Queue<Action>(10);
        private static EventHandlerProxy proxy;

        public Result OnStartup(UIControlledApplication application)
        {
            // Revit2015+ has disabled hardware acceleration for WPF to
            // avoid issues with rendering certain elements in the Revit UI. 
            // Here we get it back, by setting the ProcessRenderMode to Default,
            // signifying that we want to use hardware rendering if it's 
            // available.

            RenderOptions.ProcessRenderMode = RenderMode.Default;

            try
            {
                UIControlledApplication = application;
                ControlledApplication = application.ControlledApplication;

                SubscribeAssemblyResolvingEvent();
                SubscribeApplicationEvents();

                TransactionManager.SetupManager(new AutomaticTransactionStrategy());
                ElementBinder.IsEnabled = true;

                // Create new ribbon panel
                var panels = application.GetRibbonPanels();
                var ribbonPanel = panels.FirstOrDefault(p => p.Name.Contains(Resources.App_Description));
                if(null == ribbonPanel)
                    ribbonPanel = application.CreateRibbonPanel(Resources.App_Description);

                var fvi = FileVersionInfo.GetVersionInfo(assemblyName);
                var dynVersion = String.Format(Resources.App_Name, fvi.FileMajorPart, fvi.FileMinorPart);

                DynamoButton =
                    (PushButton)
                        ribbonPanel.AddItem(
                            new PushButtonData(
                                dynVersion,
                                dynVersion,
                                assemblyName,
                                "Dynamo.Applications.DynamoRevit"));

                Bitmap dynamoIcon = Resources.logo_square_32x32;

                BitmapSource bitmapSource =
                    Imaging.CreateBitmapSourceFromHBitmap(
                        dynamoIcon.GetHbitmap(),
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                DynamoButton.LargeImage = bitmapSource;
                DynamoButton.Image = bitmapSource;

                RegisterAdditionalUpdaters(application);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            UnsubscribeAssemblyResolvingEvent();
            UnsubscribeApplicationEvents();

            return Result.Succeeded;
        }

        private static void OnApplicationIdle(object sender, IdlingEventArgs e)
        {
            if (!idleActionQueue.Any())
                return;

            Action pendingAction = null;
            lock (idleActionQueue)
            {
                pendingAction = idleActionQueue.Dequeue();
            }

            if (pendingAction != null)
                pendingAction();
        }


        /// <summary>
        /// Add an action to run when the application is in the idle state
        /// </summary>
        /// <param name="a"></param>
        public static void AddIdleAction(Action a)
        {
            // If we are running in test mode, invoke 
            // the action immediately.
            if (DynamoModel.IsTestMode)
            {
                a.Invoke();
            }
            else
            {
                lock (idleActionQueue)
                {
                    idleActionQueue.Enqueue(a);
                }
            }
        }

        public static EventHandlerProxy EventHandlerProxy
        {
            get { return proxy; }
        }

        // should be handled by the ModelUpdater class. But there are some
        // cases where the document modifications handled there do no catch
        // certain document interactions. Those should be registered here.
        /// <summary>
        ///     Register some document updaters. Generally, document updaters
        /// </summary>
        /// <param name="application"></param>
        private static void RegisterAdditionalUpdaters(UIControlledApplication application)
        {
            var sunUpdater = new SunPathUpdater(application.ActiveAddInId);

            if (!UpdaterRegistry.IsUpdaterRegistered(sunUpdater.GetUpdaterId()))
                UpdaterRegistry.RegisterUpdater(sunUpdater);

            var sunFilter = new ElementClassFilter(typeof(SunAndShadowSettings));
            var filterList = new List<ElementFilter> { sunFilter };
            ElementFilter filter = new LogicalOrFilter(filterList);
            UpdaterRegistry.AddTrigger(
                sunUpdater.GetUpdaterId(),
                filter,
                Element.GetChangeTypeAny());
            Updaters.Add(sunUpdater);
        }

        private void SubscribeApplicationEvents()
        {
            UIControlledApplication.Idling += OnApplicationIdle;

            proxy = new EventHandlerProxy();

            UIControlledApplication.ViewActivated += proxy.OnApplicationViewActivated;
            UIControlledApplication.ViewActivating += proxy.OnApplicationViewActivating;

            ControlledApplication.DocumentClosing += proxy.OnApplicationDocumentClosing;
            ControlledApplication.DocumentClosed += proxy.OnApplicationDocumentClosed;
            ControlledApplication.DocumentOpened += proxy.OnApplicationDocumentOpened;
        }

        private void UnsubscribeApplicationEvents()
        {
            UIControlledApplication.Idling -= OnApplicationIdle;

            UIControlledApplication.ViewActivated -= proxy.OnApplicationViewActivated;
            UIControlledApplication.ViewActivating -= proxy.OnApplicationViewActivating;

            ControlledApplication.DocumentClosing -= proxy.OnApplicationDocumentClosing;
            ControlledApplication.DocumentClosed -= proxy.OnApplicationDocumentClosed;
            ControlledApplication.DocumentOpened -= proxy.OnApplicationDocumentOpened;

            proxy = null;
        }

        private void SubscribeAssemblyResolvingEvent()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }

        private void UnsubscribeAssemblyResolvingEvent()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
        }

        /// <summary>
        /// Handler to the ApplicationDomain's AssemblyResolve event.
        /// If an assembly's location cannot be resolved, an exception is
        /// thrown. Failure to resolve an assembly will leave Dynamo in 
        /// a bad state, so we should throw an exception here which gets caught 
        /// by our unhandled exception handler and presents the crash dialogue.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            var assemblyPath = string.Empty;
            var assemblyName = new AssemblyName(args.Name).Name + ".dll";

            try
            {
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

                // Try "Dynamo 0.x\Revit_20xx" folder first...
                assemblyPath = Path.Combine(assemblyDirectory, assemblyName);
                if (!File.Exists(assemblyPath))
                {
                    // If assembly cannot be found, try in "Dynamo 0.x" folder.
                    var parentDirectory = Directory.GetParent(assemblyDirectory);
                    assemblyPath = Path.Combine(parentDirectory.FullName, assemblyName);
                }

                return (File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("The location of the assembly, {0} could not be resolved for loading.", assemblyPath), ex);
            }
        }
    }
}