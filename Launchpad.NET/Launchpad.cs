﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Launchpad.NET.Effects;
using Launchpad.NET.Models;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Reactive.Disposables;
using System.Reactive;

namespace Launchpad.NET
{
    public interface ILaunchpad { }

    public abstract class Launchpad
    {
        protected Dictionary<ILaunchpadEffect, CompositeDisposable> effectsDisposables;
        protected Dictionary<ILaunchpadEffect, Timer> effectsTimers;
        protected List<LaunchpadButton> gridButtons;
        protected MidiInPort inPort;
        protected IMidiOutPort outPort;
        protected List<LaunchpadButton> sideButtons;
        protected List<LaunchpadTopButton> topButtons;
        protected readonly Subject<ILaunchpadButton> whenButtonStateChanged = new Subject<ILaunchpadButton>();

        public ObservableCollection<ILaunchpadEffect> Effects { get; protected set; }
        public string Name { get; set; }
        /// <summary>
        /// Observable event for when a button on the launchpad is pressed or released
        /// </summary>
        public IObservable<ILaunchpadButton> WhenButtonStateChanged => whenButtonStateChanged;

        public Launchpad()
        {
            effectsDisposables = new Dictionary<ILaunchpadEffect, CompositeDisposable>();
            effectsTimers = new Dictionary<ILaunchpadEffect, Timer>();
        }

        void OnChangeEffectUpdateFrequency(ILaunchpadEffect effect, int newFrequency)
        {
            effectsTimers[effect].Change(0, newFrequency);
        }

        void OnEffectComplete(ILaunchpadEffect effect)
        {
            UnregisterEffect(effect);
        }

        /// <summary>
        /// Add an effect to the launchpad
        /// </summary>
        /// <param name="effect"></param>
        /// <param name="updateFrequency"></param>
        public void RegisterEffect(ILaunchpadEffect effect, TimeSpan updateFrequency)
        {
            try
            {
                // Add the effect to the launchpad
                Effects.Add(effect);

                // Register any observables being used
                CompositeDisposable effectDisposables = new CompositeDisposable();

                // If this effect needs the ability to change its frequency
                if(effect.WhenChangeUpdateFrequency != null)
                {
                    // Subscribe to the event to change the frequency and add it to this effects disposables
                    effectDisposables.Add(
                        effect
                        .WhenChangeUpdateFrequency
                        .Subscribe(newFrequency=>
                        {
                            // Change the frequency for this effect
                            OnChangeEffectUpdateFrequency(effect, newFrequency);
                        }));
                }

                // If this effect will notify us it needs to be unregistered
                if(effect.WhenComplete != null)
                {
                    effectDisposables.Add(
                        effect
                        .WhenComplete
                        .Subscribe(_ => 
                        {
                            // Unregister the effect and destroy its disposables
                            OnEffectComplete(effect);
                        }));
                }

                // Create an update timer at the specified frequency
                effectsTimers.Add(effect, new Timer(state => effect.Update(), null, 0, (int)updateFrequency.TotalMilliseconds));

                // Initiate the effect (provide all buttons and button changed event
                effect.Initiate(this);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

        }
        public abstract void SendMessage(IMidiMessage message);

        public abstract void UnregisterEffect(ILaunchpadEffect effect);
    }

    public static class Novation
    {
        public static async Task<Launchpad> Launchpad(string inputDeviceName = "launchpad", string outputDeviceName = "launchpad")
        {
            try
            {
                // Get all input MIDI devices
                var midiInputDevices = await DeviceInformation.FindAllAsync(MidiInPort.GetDeviceSelector());
                midiInputDevices.ToList().ForEach(device => Debug.WriteLine($"Found input device: {device.Name}"));
                // Get all output MIDI devices
                var midiOutputDevices = await DeviceInformation.FindAllAsync(MidiOutPort.GetDeviceSelector());
                midiInputDevices.ToList().ForEach(device => Debug.WriteLine($"Found output device: {device.Name}"));

                // Find the launchpad input
                foreach (var inputDeviceInfo in midiInputDevices)
                {
                    Debug.WriteLine("INPUT: " + inputDeviceInfo.Name);
                    if (inputDeviceInfo.Name.ToLower().Contains(inputDeviceName.ToLower()))
                    {
                        // Find the launchpad output 
                        foreach (var outputDeviceInfo in midiOutputDevices)
                        {
                            Debug.WriteLine("OUTPUT: " + outputDeviceInfo.Name);
                            // If not a match continue
                            if (!outputDeviceInfo.Name.ToLower().Contains(outputDeviceName.ToLower())) continue;

                            var inPort = await MidiInPort.FromIdAsync(inputDeviceInfo.Id);
                            var outPort = await MidiOutPort.FromIdAsync(outputDeviceInfo.Id);

                            // Return an MK2 if detected
                            if (outputDeviceInfo.Name.ToLower().Contains("mk2"))
                                return new LaunchpadMk2(outputDeviceInfo.Name, inPort, outPort);

                            return null;
                            // Otherwise return Standard
                            //return new LaunchpadS(outputDeviceInfo.Name, inPort, outPort);
                        }
                    }
                }

                // Return null if no devices matched the device name provided
                return null;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }
    }
}
