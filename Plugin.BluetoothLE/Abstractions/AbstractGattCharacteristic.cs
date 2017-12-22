﻿using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;


namespace Plugin.BluetoothLE
{
    public abstract class AbstractGattCharacteristic : IGattCharacteristic
    {
        protected AbstractGattCharacteristic(IGattService service, Guid uuid, CharacteristicProperties properties)
        {
            this.Service = service;
            this.Uuid = uuid;
            this.Properties = properties;
        }


        protected Subject<CharacteristicGattResult> ReadSubject { get; } = new Subject<CharacteristicGattResult>();
        protected Subject<CharacteristicGattResult> WriteSubject { get; } = new Subject<CharacteristicGattResult>();

        public IGattService Service { get; }
        public virtual string Description => Dictionaries.GetCharacteristicDescription(this.Uuid);
        public bool IsNotifying { get; protected set; }
        public Guid Uuid { get; }
        public CharacteristicProperties Properties { get; }
        public byte[] Value { get; protected set; }

        public abstract IObservable<CharacteristicGattResult> EnableNotifications(bool enableIndicationsIfAvailable);
        public abstract IObservable<CharacteristicGattResult> DisableNotifications();
        public abstract IObservable<IGattDescriptor> WhenDescriptorDiscovered();
        public abstract IObservable<CharacteristicGattResult> Read();
        public abstract void WriteWithoutResponse(byte[] value);
        public abstract IObservable<CharacteristicGattResult> Write(byte[] value);

        public abstract IObservable<CharacteristicGattResult> WhenNotificationReceived();
        public virtual IObservable<CharacteristicGattResult> WhenRead() => this.ReadSubject;
        public virtual IObservable<CharacteristicGattResult> WhenWritten() => this.WriteSubject;


        public virtual IObservable<BleWriteSegment> BlobWrite(byte[] value, bool reliableWrite)
            // don't need to dispose of memorystream
            => this.BlobWrite(new MemoryStream(value), reliableWrite);


        public virtual IObservable<BleWriteSegment> BlobWrite(Stream stream, bool reliableWrite)
            => Observable.Create<BleWriteSegment>(async ob =>
            {
                var cts = new CancellationTokenSource();
                var trans = reliableWrite
                    ? this.Service.Device.BeginReliableWriteTransaction()
                    : new VoidGattReliableWriteTransaction();

                using (trans)
                {
                    var mtu = this.Service.Device.GetCurrentMtuSize();
                    var buffer = new byte[mtu];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    var pos = read;
                    var len = Convert.ToInt32(stream.Length);

                    while (!cts.IsCancellationRequested && read > 0)
                    {
                        await trans.Write(this, buffer).RunAsync(cts.Token);
                        //await this.Write(buffer).RunAsync(cts.Token);
                        if (this.Value != buffer)
                        {
                            trans.Abort();
                            throw new GattReliableWriteTransactionException("There was a mismatch response");
                        }
                        var seg = new BleWriteSegment(buffer, pos, len);
                        ob.OnNext(seg);

                        read = stream.Read(buffer, 0, buffer.Length);
                        pos += read;
                    }
                    await trans.Commit();
                }
                ob.OnCompleted();

                return () =>
                {
                    cts.Cancel();
                    trans.Dispose();
                };
            });
    }
}
