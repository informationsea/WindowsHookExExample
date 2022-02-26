using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;

namespace WindowsHookExExample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private KeyboardHook keyboardHook;
        private bool consumeAllEvents = false;

        public MainWindow()
        {
            InitializeComponent();
            keyboardHook = new KeyboardHook(HookEvent);
        }

        private void AddMessage(string message)
        {
            textBox.Text = message + textBox.Text;
        }

        private bool HookEvent(KeyboardHook.HookMessage message, uint vkCode, uint scanCode, KeyboardHook.KBDLLHOOKSTRUCTFlags flags, uint time)
        {
            // This function is called in non-UI thread

            Dispatcher.BeginInvoke(() =>
            {
                var hookMessageText = "";
                switch (message)
                {
                    case KeyboardHook.HookMessage.WM_KEYUP:
                        hookMessageText = "    Key up  ";
                        break;
                    case KeyboardHook.HookMessage.WM_KEYDOWN:
                        hookMessageText = "    Key down";
                        break;
                    case KeyboardHook.HookMessage.WM_SYSKEYUP:
                        hookMessageText = "Sys Key up  ";
                        break;
                    case KeyboardHook.HookMessage.WM_SYSKEYDOWN:
                        hookMessageText = "Sys Key down";
                        break;
                }
                AddMessage($"{time} {hookMessageText} vkCode:{vkCode:x2} scanCode:{scanCode:x2}\n");
            });

            return consumeAllEvents;
        }

        private void CheckboxHookChanged(object sender, RoutedEventArgs e)
        {
            if (checkboxHook.IsChecked == true)
            {
                textBox.Text = "Start\n" + textBox.Text;
                keyboardHook.StartHook();
            }
            else
            {
                textBox.Text = "Stop\n" + textBox.Text;
                keyboardHook.StopHook();
            }
        }

        private void ConsumeChanged(object sender, RoutedEventArgs e)
        {
            consumeAllEvents = checkboxConsume.IsChecked == true;
        }


        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            keyboardHook.StopThread();
        }
    }

    class KeyboardHook
    {
        private IntPtr hookHandle;
        private KeyEventDelegate callback;
        private uint threadId = 0;
        private Thread thread;

        public KeyboardHook(KeyEventDelegate callback)
        {
            hookHandle = IntPtr.Zero;
            this.callback = callback;
            thread = new Thread(ThreadLoop);
            thread.IsBackground = true;
            thread.Start(Dispatcher.CurrentDispatcher);
        }

        ~KeyboardHook()
        {
            PostThreadMessage(threadId, WM_END_LOOP, UIntPtr.Zero, IntPtr.Zero);
        }

        public delegate bool KeyEventDelegate(HookMessage message, uint vkCode, uint scanCode, KBDLLHOOKSTRUCTFlags flags, uint time);

        private void SetThreadId(uint id)
        {
            threadId = id;
            //Trace.WriteLine($"set thread id: {Environment.CurrentManagedThreadId} / {GetCurrentThreadId()} -> {id}");
        }

        private void ThreadLoop(object? dispatcherObj)
        {
            Dispatcher? dispatcher = (Dispatcher?)dispatcherObj;
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.BeginInvoke(SetThreadId, GetCurrentThreadId());
            IntPtr hInstance = Marshal.GetHINSTANCE(System.Reflection.Assembly.GetExecutingAssembly().Modules.First());
            IntPtr hookPtr = IntPtr.Zero;

            IntPtr msgPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Msg)));
            int bRet;
            while ((bRet = GetMessage(msgPtr, IntPtr.Zero, 0, 0)) != 0)
            {
                if (bRet == -1)
                {
                    Trace.WriteLine("Message Loop Error");
                    break;
                }

                Msg? msg = Marshal.PtrToStructure<Msg>(msgPtr);
                if (msg == null)
                {
                    continue;
                }


                switch (msg.message)
                {
                    case WM_START_HOOK:
                        if (hookHandle == IntPtr.Zero)
                        {
                            hookHandle = SetWindowsHookEx(HookId.KeyboardLL, new HookProc((int code, IntPtr wParam, IntPtr lParam) =>
                            {
                                HookMessage message = (HookMessage)wParam;
                                var kbdHookStructure = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                                if (kbdHookStructure != null)
                                {
                                    if (callback(message, kbdHookStructure.vkCode, kbdHookStructure.scanCode, kbdHookStructure.flags, kbdHookStructure.time))
                                    {
                                        return (IntPtr)1;
                                    }
                                }
                                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
                            }
                            ), hInstance, 0);
                        }
                        break;
                    case WM_STOP_HOOK:
                        if (hookHandle != IntPtr.Zero)
                        {
                            UnhookWindowsHookEx(hookHandle);
                            hookHandle = IntPtr.Zero;
                        }
                        break;
                    case WM_END_LOOP:
                        if (hookHandle != IntPtr.Zero)
                        {
                            UnhookWindowsHookEx(hookHandle);
                            hookHandle = IntPtr.Zero;
                        }
                        Trace.WriteLine("end of message loop 1");
                        return;
                    default:
                        TranslateMessage(msgPtr);
                        DispatchMessage(msgPtr);
                        break;
                }

            }
            Trace.WriteLine("end of message loop 2");
        }



        public void StartHook()
        {
            PostThreadMessage(threadId, WM_START_HOOK, UIntPtr.Zero, IntPtr.Zero);
        }

        public void StopHook()
        {
            PostThreadMessage(threadId, WM_STOP_HOOK, UIntPtr.Zero, IntPtr.Zero);
        }

        public void StopThread()
        {
            PostThreadMessage(threadId, WM_END_LOOP, UIntPtr.Zero, IntPtr.Zero);
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        public enum HookMessage : uint
        {
            WM_KEYDOWN = 0x0100,
            WM_KEYUP = 0x0101,
            WM_SYSKEYDOWN = 0x0104,
            WM_SYSKEYUP = 0x0105
        }

        [StructLayout(LayoutKind.Sequential)]
        public class KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public KBDLLHOOKSTRUCTFlags flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [Flags]
        public enum KBDLLHOOKSTRUCTFlags : uint
        {
            LLKHF_EXTENDED = 0x01,
            LLKHF_INJECTED = 0x10,
            LLKHF_ALTDOWN = 0x20,
            LLKHF_UP = 0x80,
        }

        [StructLayout(LayoutKind.Sequential)]
        public class Point
        {
            public int x;
            public int y;
        }


        [StructLayout(LayoutKind.Sequential)]
        public class Msg
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public int time;
            public Point pt;
            public int lPrivate;
        }

        internal enum HookId : uint
        {
            KeyboardLL = 13,
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(HookId idHook, HookProc hook, IntPtr hInstance, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hookHandle, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern int GetMessage(IntPtr msg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(IntPtr lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(IntPtr lpmsg);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool PostMessage(HandleRef hWnd, [In] ref Msg msg, IntPtr wParam, IntPtr lParam);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostThreadMessage(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        const uint WM_USER = 0x0400;
        const uint WM_START_HOOK = 0x0401;
        const uint WM_STOP_HOOK = 0x0402;
        const uint WM_END_LOOP = 0x0403;
    }
}
