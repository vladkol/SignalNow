using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.SignalNow.Client
{
    public class SignalNowMessageAction
    {
        public bool Waiting 
        {
            get
            {
                return !cancellationToken.IsCancellationRequested && !Started && !Cancelled;
            }
        }

        public bool Started { get; private set; } = false;
        public bool Completed { get; private set; } = false;
        public bool Cancelled
        {
            get
            {
                return cancelled || cancellationToken.IsCancellationRequested;
            }
        }

        private readonly Action action;
        private CancellationToken cancellationToken;
        private bool cancelled = false;


        internal SignalNowMessageAction(SignalNowClient client, 
                                        string recipient, bool groupRecipient, string messageType, 
                                        string messagePayload, bool payloadIsJson, CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
            client.ConnectionChanged += Client_ConnectionChanged;

            this.action = new Action(() =>
            {
                Started = true;
                try
                {
                    client.SendMessage(recipient, groupRecipient, messageType, messagePayload, payloadIsJson).Wait(cancellationToken);
                }
                finally
                {
                    Completed = true;
                }
            });
        }

        public bool Cancel()
        {
            if (!Started)
            {
                cancelled = true;
            }

            return cancelled;
        }

        internal void Run()
        {
            if(!Cancelled)
            {
                action.Invoke();
            }
        }

        internal Task RunAsync()
        {
            return Task.Run(new Action(Run));
        }

        void Client_ConnectionChanged(SignalNowClient signalNow, bool connected, Exception ifErrorWhy)
        {
            if (!connected)
            {
                cancelled = true;
            }
        }
    }
}