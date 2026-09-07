using ClinicNow.Model.Configuration;
using ClinicNow.Worker;
using ClinicNow.Worker.Mail;
using ClinicNow.Worker.Messaging;
using ClinicNow.Worker.Push;
using Microsoft.Extensions.Hosting;

// Load .env before anything else reads configuration - same convention (and same
// TraversePath() rationale) as ClinicNow.API/Program.cs. In Docker, these arrive as
// real container env vars instead (docker-compose.yml), and this no-ops if no .env
// file is found anywhere up the directory tree.
DotNetEnv.Env.TraversePath().Load();

var builder = Host.CreateApplicationBuilder(args);

// Centralized configuration (rulebook Part II §C/§D) - read once here, injected
// everywhere else. See ClinicNow.Model.Configuration.EnvOptionsBase.
var rabbitMqOptions = new RabbitMqOptions();
var smtpOptions = new SmtpOptions();
var firebaseOptions = new FirebaseOptions();

builder.Services.AddSingleton(rabbitMqOptions);
builder.Services.AddSingleton(smtpOptions);
builder.Services.AddSingleton(firebaseOptions);

builder.Services.AddSingleton<RabbitMqConnectionProvider>();
builder.Services.AddScoped<IMailSender, MailSender>();

// IHttpClientFactory, never `new HttpClient()` (rulebook Part II §D).
builder.Services.AddHttpClient();
builder.Services.AddScoped<IPushSender, FcmPushSender>();

// If MailQueueConsumerWorker.ExecuteAsync throws (e.g. RabbitMQ connection retries
// exhausted), stop the host instead of leaving a zombie process behind - so
// Docker's `restart: unless-stopped` policy actually kicks in, rather than the
// container silently going idle and unreachable (rulebook Appendix A.1: "worker ne
// smije tiho postati nedostupan bez logiranja razloga").
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

builder.Services.AddHostedService<MailQueueConsumerWorker>();
builder.Services.AddHostedService<PushQueueConsumerWorker>();

var host = builder.Build();
host.Run();
