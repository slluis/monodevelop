// DispatchService.cs
//
// Author:
//   Todd Berman  <tberman@off.net>
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2005 Todd Berman  <tberman@off.net>
// Copyright (c) 2005 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Threading;
using System.Collections;

using MonoDevelop.Core;
using MonoDevelop.Ide.Gui;
using System.Collections.Generic;


// Testing

namespace MonoDevelop.Ide
{
	public class DispatchService
	{
		static Queue<GenericMessageContainer> backgroundQueue = new Queue<GenericMessageContainer> ();
		static ManualResetEvent backgroundThreadWait = new ManualResetEvent (false);
		static Queue<GenericMessageContainer> guiQueue = new Queue<GenericMessageContainer> ();
		static Thread thrBackground;
		static uint iIdle = 0;
		static GLib.TimeoutHandler handler;
		static Thread guiThread;
		static GuiSyncContext guiContext;
		static internal bool DispatchDebug;
		const string errormsg = "An exception was thrown while dispatching a method call in the UI thread.";

		internal static void Initialize ()
		{
			guiContext = new GuiSyncContext ();
			guiThread = Thread.CurrentThread;
			
			handler = new GLib.TimeoutHandler (guiDispatcher);
			
			thrBackground = new Thread (new ThreadStart (backgroundDispatcher)) {
				Name = "Background dispatcher",
				IsBackground = true,
				Priority = ThreadPriority.Lowest,
			};
			thrBackground.Start ();
			
			DispatchDebug = Environment.GetEnvironmentVariable ("MONODEVELOP_DISPATCH_DEBUG") != null;
		}
		
		public static void GuiDispatch (MessageHandler cb)
		{
			if (IsGuiThread) {
				cb ();
				return;
			}

			QueueMessage (new GenericMessageContainer (cb, false));
		}

		public static void GuiDispatch (StatefulMessageHandler cb, object state)
		{
			if (IsGuiThread) {
				cb (state);
				return;
			}

			QueueMessage (new StatefulMessageContainer (cb, state, false));
		}

		public static void GuiSyncDispatch (MessageHandler cb)
		{
			if (IsGuiThread) {
				cb ();
				return;
			}

			GenericMessageContainer mc = new GenericMessageContainer (cb, true);
			lock (mc) {
				QueueMessage (mc);
				Monitor.Wait (mc);
			}
			if (mc.Exception != null)
				throw new Exception (errormsg, mc.Exception);
		}
		
		public static void GuiSyncDispatch (StatefulMessageHandler cb, object state)
		{
			if (IsGuiThread) {
				cb (state);
				return;
			}

			StatefulMessageContainer mc = new StatefulMessageContainer (cb, state, true);
			lock (mc) {
				QueueMessage (mc);
				Monitor.Wait (mc);
			}
			if (mc.Exception != null)
				throw new Exception (errormsg, mc.Exception);
		}
		
		public static void RunPendingEvents ()
		{
			// The loop is limited to 1000 iterations as a workaround for an issue that some users
			// have experienced. Sometimes EventsPending starts return 'true' for all iterations,
			// causing the loop to never end.
			
			int n = 1000;
			Gdk.Threads.Enter();
			while (Gtk.Application.EventsPending () && --n > 0) {
				Gtk.Application.RunIteration (false);
			}
			Gdk.Threads.Leave();
			guiDispatcher ();
		}
		
		static void QueueMessage (GenericMessageContainer msg)
		{
			lock (guiQueue) {
				guiQueue.Enqueue (msg);
				if (iIdle == 0)
					iIdle = GLib.Timeout.Add (0, handler);
			}
		}
		
		public static bool IsGuiThread
		{
			get { return guiThread == Thread.CurrentThread; }
		}
		
		public static void AssertGuiThread ()
		{
			if (guiThread != Thread.CurrentThread)
				throw new InvalidOperationException ("This method can only be called in the GUI thread");
		}
		
		public static Delegate GuiDispatch (Delegate del)
		{
			return guiContext.CreateSynchronizedDelegate (del);
		}
		
		public static T GuiDispatch<T> (T theDelegate)
		{
			if (guiContext == null)
				return theDelegate;
			Delegate del = (Delegate)(object)theDelegate;
			return (T)(object)guiContext.CreateSynchronizedDelegate (del);
		}
		
		/// <summary>
		/// Runs the provided delegate in the background, but waits until finished, pumping the
		/// message queue if necessary.
		/// </summary>
		public static void BackgroundDispatchAndWait (MessageHandler cb)
		{
			object eventObject = new object ();
			lock (eventObject) {
				BackgroundDispatch (delegate {
					try {
						cb ();
					} finally {
						lock (eventObject) {
							Monitor.Pulse (eventObject);
						}
					}
				});
				if (IsGuiThread) {
					while (true) {
						if (Monitor.Wait (eventObject, 50))
							return;
						RunPendingEvents ();
					}
				}
				else {
					Monitor.Wait (eventObject);
				}
			}
		}
		
		public static void BackgroundDispatch (MessageHandler cb)
		{
			QueueBackground (new GenericMessageContainer (cb, false));
		}

		public static void BackgroundDispatch (StatefulMessageHandler cb, object state)
		{
			QueueBackground (new StatefulMessageContainer (cb, state, false));
		}
		
		static void QueueBackground (GenericMessageContainer c)
		{
			lock (backgroundQueue) {
				backgroundQueue.Enqueue (c);
				if (backgroundQueue.Count == 1)
					backgroundThreadWait.Set ();
			}
		}
		
		public static void ThreadDispatch (MessageHandler cb)
		{
			GenericMessageContainer smc = new GenericMessageContainer (cb, false);
			Thread t = new Thread (new ThreadStart (smc.Run));
			t.Name = "Message dispatcher";
			t.IsBackground = true;
			t.Start ();
		}

		public static void ThreadDispatch (StatefulMessageHandler cb, object state)
		{
			StatefulMessageContainer smc = new StatefulMessageContainer (cb, state, false);
			Thread t = new Thread (new ThreadStart (smc.Run));
			t.Name = "Message dispatcher";
			t.IsBackground = true;
			t.Start ();
		}

		static bool guiDispatcher ()
		{
			GenericMessageContainer msg;
			int iterCount;
			
			lock (guiQueue) {
				iterCount = guiQueue.Count;
				if (iterCount == 0) {
					iIdle = 0;
					return false;
				}
			}
			
			for (int n=0; n<iterCount; n++) {
				lock (guiQueue) {
					if (guiQueue.Count == 0) {
						iIdle = 0;
						return false;
					}
					msg = guiQueue.Dequeue ();
				}
				
				msg.Run ();
				
				if (msg.IsSynchronous)
					lock (msg) Monitor.PulseAll (msg);
				else if (msg.Exception != null)
					HandlerError (msg);
			}
			
			lock (guiQueue) {
				if (guiQueue.Count == 0) {
					iIdle = 0;
					return false;
				} else
					return true;
			}
		}

		static void backgroundDispatcher ()
		{
			while (true) {
				GenericMessageContainer msg = null;
				bool wait = false;
				lock (backgroundQueue) {
					if (backgroundQueue.Count == 0) {
						backgroundThreadWait.Reset ();
						wait = true;
					} else
						msg = backgroundQueue.Dequeue ();
				}
				
				if (wait) {
					WaitHandle.WaitAll (new WaitHandle[] {backgroundThreadWait});
					continue;
				}
				
				if (msg != null) {
					msg.Run ();
					if (msg.Exception != null)
						HandlerError (msg);
				}
			}
		}
		
		static void HandlerError (GenericMessageContainer msg)
		{
			if (msg.CallerStack != null) {
				LoggingService.LogError ("{0} {1}\nCaller stack:{2}", errormsg, msg.Exception.ToString (), msg.CallerStack);
			}
			else
				LoggingService.LogError ("{0} {1}\nCaller stack not available. Define the environment variable MONODEVELOP_DISPATCH_DEBUG to enable caller stack capture.", errormsg, msg.Exception.ToString ());
		}
	}

	public delegate void MessageHandler ();
	public delegate void StatefulMessageHandler (object state);

	class GenericMessageContainer
	{
		MessageHandler callback;
		protected Exception ex;
		protected bool isSynchronous;
		protected string callerStack;

		protected GenericMessageContainer () { }

		public GenericMessageContainer (MessageHandler cb, bool isSynchronous)
		{
			callback = cb;
			this.isSynchronous = isSynchronous;
			if (DispatchService.DispatchDebug) callerStack = Environment.StackTrace;
		}

		public virtual void Run ( )
		{
			try {
				callback ();
			}
			catch (Exception e) {
				ex = e;
			}
		}
		
		public Exception Exception
		{
			get { return ex; }
		}
		
		public bool IsSynchronous
		{
			get { return isSynchronous; }
		}
		
		public string CallerStack
		{
			get { return callerStack; }
		}
	}

	class StatefulMessageContainer : GenericMessageContainer
	{
		object data;
		StatefulMessageHandler callback;

		public StatefulMessageContainer (StatefulMessageHandler cb, object state, bool isSynchronous)
		{
			data = state;
			callback = cb;
			this.isSynchronous = isSynchronous;
			if (DispatchService.DispatchDebug) callerStack = Environment.StackTrace;
		}

		public override void Run ( )
		{
			try {
				callback (data);
			}
			catch (Exception e) {
				ex = e;
			}
		}
	}

}
