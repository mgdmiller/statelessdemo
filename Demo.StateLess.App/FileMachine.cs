using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Stateless;

namespace Demo.StateLess.App
{
    public class FileMachine
    {
        private const int MaxRetry = 5;

        public enum State
        {
            Accepted,
            Pending,
            Completed,
            Failed
        }

        public enum Trigger
        {
            Accept,
            Reject,
            Fail,
            Send,
            Complete
        }

        /// <summary>
        /// Current file state
        /// </summary>
        private State _state;

        /// <summary>
        /// Количество повторов
        /// </summary>
        private int _retry;

        /// <summary>
        /// Урл отрпавки файла
        /// </summary>
        private string _url;

        /// <summary>
        /// Экземпляр машины
        /// </summary>
        private readonly StateMachine<State, Trigger> _machine;

        /// <summary>
        /// Триггер режекта сервером
        /// </summary>
        private StateMachine<State, Trigger>.TriggerWithParameters<Exception> _rejectTrigger;

        /// <summary>
        /// File content
        /// </summary>
        public FileInformation File { get; }

        public FileMachine(FileInformation information)
        {
            File = information;
            _state = State.Accepted;
            _machine = new StateMachine<State, Trigger>(() => _state, state => _state = state);
            _rejectTrigger =
                new StateMachine<State, Trigger>.TriggerWithParameters<Exception>(Trigger.Reject);

            #region States configuration

            /*
             * Начальное состояние когда файл принят.
             * Ожидает сигнала к отправке файла
             */
            _machine.Configure(State.Accepted)
                .OnEntry(OnEntry)
                .OnActivate(OnActivate)
                .Permit(Trigger.Send, State.Pending)
                .OnDeactivate(OnDeactivate)
                .OnExit(OnExit);

            /*
             * Состояние отправки файла на cервер
             * Разрешает три триггера
             */
            _machine.Configure(State.Pending)
                .OnEntry(OnEntry)
                .OnEntryAsync(() => SendPayloadToService(_url))
                .OnEntryFrom(_rejectTrigger, exception => Console.WriteLine("Delivery error: {0}", exception.Message))
                .OnActivate(OnActivate)
                .Permit(Trigger.Fail, State.Failed)
                .Permit(Trigger.Complete, State.Completed)
                .PermitIf(Trigger.Reject, State.Failed, () => _retry >= MaxRetry)
                .PermitReentryIf(Trigger.Reject, () => _retry < MaxRetry, "Retry limit exceeded")
                .OnDeactivate(OnDeactivate)
                .OnExit(() => _retry++)
                .OnExit(OnExit);

            /*
             * Состояние ошибки
             */
            _machine.Configure(State.Failed)
                .OnEntry(OnEntry)
                .OnEntry(() => Console.WriteLine("File sending failed with retry count: {0}", _retry))
                .OnActivate(OnActivate)
                .OnDeactivate(OnDeactivate)
                .OnExit(OnExit);

            /*
             * Состояние завершения
             */
            _machine.Configure(State.Completed)
                .OnEntry(OnEntry)
                .OnEntry(() => Console.WriteLine("File sending succeed with retry count: {0}", _retry))
                .OnActivate(OnActivate)
                .OnDeactivate(OnDeactivate)
                .OnExit(OnExit);

            #endregion
        }

        /// <summary>
        /// State entering event
        /// </summary>
        private void OnEntry()
        {
            Console.WriteLine($"Entering {_state.ToString()} ...");
        }

        /// <summary>
        /// State activating event
        /// </summary>
        private void OnActivate()
        {
            Console.WriteLine($"Activating {_state.ToString()} ...");
        }

        /// <summary>
        /// State deactivating event
        /// </summary>
        private void OnDeactivate()
        {
            Console.WriteLine($"Deactivating {_state.ToString()} ...");
        }

        /// <summary>
        /// State exiting event
        /// </summary>
        private void OnExit()
        {
            Console.WriteLine($"Exiting {_state.ToString()} ...");
        }

        private async Task SendPayloadToService(string serviceUrl)
        {
            try
            {
                Console.WriteLine("[{0}]Sending payload to service: {1}", _retry, serviceUrl);

                if (_retry > 0)
                {
                    var timeout = 1000 * _retry;
                    Console.WriteLine("Delaying delivery for {0} second", _retry);
                    await Task.Delay(timeout);
                }

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var message = await client.PostAsync(_url,
                        new StringContent(JsonConvert.SerializeObject(File), Encoding.UTF8, "application/json"));

                    // Is all fine complete operation
                    if (message.IsSuccessStatusCode)
                        _machine.Fire(Trigger.Complete);

                    // Is server status is bad reject
                    if (message.StatusCode >= (HttpStatusCode) 400)
                        _machine.Fire(Trigger.Fail);
                }
            }
            catch (Exception ex)
            {
                // If operation failed for unknown reasons - retry it
                _machine.Fire(_rejectTrigger, ex);
            }
        }

        /// <summary>
        /// Выполнение отправки файла
        /// </summary>
        /// <param name="url"></param>
        public Task Send(string url)
        {
            _url = url;
            return _machine.FireAsync(Trigger.Send);
        }
    }
}