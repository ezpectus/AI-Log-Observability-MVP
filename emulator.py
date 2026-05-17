#!/usr/bin/env python3
import random
import time
import uuid
import requests
from datetime import datetime
from colorama import Fore, Style, init

init(autoreset=True)

API_URL = "http://localhost:5000/api/logs"

SERVICES = ["AuthService", "PaymentGateway", "DataAnalytics"]

INFO_MESSAGES = [
    "User logged in successfully",
    "User session started",
    "Database connection established",
    "Cache refreshed successfully",
    "Health check passed",
    "Configuration loaded",
    "Worker started processing queue",
    "API gateway request received",
    "Authentication token validated",
    "Data sync completed"
]

WARNING_MESSAGES = [
    "Database connection slow (response time: 2.5s)",
    "Cache miss ratio increased to 15%",
    "Memory usage above 80%",
    "API rate limit approaching (85% used)",
    "Disk space low (15% remaining)",
    "External API response time degraded",
    "Connection pool exhausted",
    "Queue size growing (500+ items)",
    "SSL certificate expiring soon",
    "Deprecated API endpoint used"
]

ERROR_SCENARIOS = [
    {
        "message": "NullReferenceException: Object reference not set to an instance of an object",
        "stack_trace": "   at PaymentGateway.Services.ChargeCard(Guid orderId) in /src/PaymentGateway/Services/PaymentService.cs:line 42\n   at PaymentController.Checkout() in /src/Api/Controllers/PaymentController.cs:line 28\n   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__140_0(Object state)\n   at System.Threading.QueueUserWorkItemCallback.Execute()"
    },
    {
        "message": "HttpRequestException: Failed to fetch currency rates from external API (Timeout)",
        "stack_trace": "   at System.Net.Http.HttpClient.SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)\n   at DataAnalytics.Worker.FetchRates() in /src/DataAnalytics/Worker/RateFetcher.cs:line 67\n   at DataAnalytics.Worker.ExecuteAsync(CancellationToken stoppingToken)\n   at Microsoft.Extensions.Hosting.Internal.Host.StartAsync(CancellationToken cancellationToken)"
    },
    {
        "message": "TimeoutException: Database query exceeded timeout of 30 seconds",
        "stack_trace": "   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken)\n   at AuthService.Repositories.UserRepository.GetUserByIdAsync(Guid userId) in /src/AuthService/Repositories/UserRepository.cs:line 89\n   at AuthService.Services.UserService.GetUserProfile(Guid userId) in /src/AuthService/Services/UserService.cs:line 34"
    },
    {
        "message": "DbUpdateException: An error occurred while updating the entries. Duplicate key violation",
        "stack_trace": "   at Microsoft.EntityFrameworkCore.Update.AffectedCountCommandReader.ConsumeRowCountWithRetryAsync(CancellationToken cancellationToken)\n   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable<ModificationCommandBatch> commandBatches, CancellationToken cancellationToken)\n   at PaymentGateway.Repositories.TransactionRepository.SaveTransactionAsync(Transaction transaction) in /src/PaymentGateway/Repositories/TransactionRepository.cs:line 56"
    },
    {
        "message": "UnauthorizedAccessException: Access denied to secure storage",
        "stack_trace": "   at System.IO.FileStream..ctor(String path, FileMode mode, FileAccess access, FileShare share, Int32 bufferSize, FileOptions options)\n   at AuthService.Services.SecretService.GetSecretAsync(String keyName) in /src/AuthService/Services/SecretService.cs:line 23\n   at AuthService.Middleware.AuthenticationMiddleware.InvokeAsync(HttpContext context)"
    },
    {
        "message": "InvalidOperationException: Sequence contains no elements",
        "stack_trace": "   at System.Linq.Enumerable.First[TSource](IEnumerable`1 source)\n   at DataAnalytics.Services.ReportService.GenerateReport(Guid reportId) in /src/DataAnalytics/Services/ReportService.cs:line 112\n   at DataAnalytics.Controllers.ReportController.GetReport(Guid id) in /src/DataAnalytics/Controllers/ReportController.cs:line 45"
    },
    {
        "message": "SocketException: Connection refused by remote host",
        "stack_trace": "   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error)\n   at System.Net.Sockets.Socket.ConnectAsync(SocketAddress[] addresses, CancellationToken cancellationToken)\n   at PaymentGateway.External.BankApiClient.ConnectAsync() in /src/PaymentGateway/External/BankApiClient.cs:line 78\n   at PaymentGateway.Services.PaymentService.ProcessPayment(PaymentRequest request)"
    },
    {
        "message": "JsonException: Invalid JSON format in request body",
        "stack_trace": "   at System.Text.Json.JsonDocument.Parse(ReadOnlySpan`1 utf8Json, JsonDocumentOptions options)\n   at AuthService.Middleware.JsonValidationMiddleware.ValidateJsonBody(HttpContext context)\n   at Microsoft.AspNetCore.Builder.UseMiddlewareExtensions.<>c__DisplayClass6_1.<UseMiddleware>b__2(HttpContext context)\n   at Microsoft.AspNetCore.Builder.ApplicationBuilder.Build()"
    }
]

CRITICAL_MESSAGES = [
    {
        "message": "OutOfMemoryException: Insufficient memory to continue the execution of the program",
        "stack_trace": "   at System.Collections.Generic.List`1.set_Capacity(Int32 value)\n   at DataAnalytics.Processors.LargeDataProcessor.LoadDataset(String filePath) in /src/DataAnalytics/Processors/LargeDataProcessor.cs:line 156\n   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)\n   at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)"
    },
    {
        "message": "StackOverflowException: The request was aborted because the request was too large",
        "stack_trace": "   at System.Runtime.CompilerServices.RuntimeHelpers.ExecuteClassConstructor(IntPtr ptr)\n   at AuthService.Services.RecursiveValidator.ValidateDeep(Object obj, Int32 depth) in /src/AuthService/Services/RecursiveValidator.cs:line 89\n   at AuthService.Services.RecursiveValidator.ValidateDeep(Object obj, Int32 depth)\n   [RECURSION LIMIT EXCEEDED]"
    }
]

def get_log_level():
    levels = [0, 1, 2, 3]
    weights = [60, 20, 15, 5]
    return random.choices(levels, weights=weights)[0]

def generate_log_entry():
    level = get_log_level()
    service = random.choice(SERVICES)
    
    if level == 0:  # Info
        message = random.choice(INFO_MESSAGES)
        stack_trace = None
    elif level == 1:  # Warning
        message = random.choice(WARNING_MESSAGES)
        stack_trace = None
    elif level == 2:  # Error
        scenario = random.choice(ERROR_SCENARIOS)
        message = scenario["message"]
        stack_trace = scenario["stack_trace"]
    else:  # Critical
        scenario = random.choice(CRITICAL_MESSAGES)
        message = scenario["message"]
        stack_trace = scenario["stack_trace"]
    
    return {
        "serviceName": service,
        "level": level,
        "message": message,
        "stackTrace": stack_trace,
        "createdAtUtc": datetime.utcnow().isoformat() + "Z",
        "errorGroupId": None
    }

def get_level_name(level):
    levels = {0: "Info", 1: "Warning", 2: "Error", 3: "Critical"}
    return levels.get(level, "Unknown")

def get_level_color(level):
    if level == 0:
        return Fore.GREEN
    elif level == 1:
        return Fore.YELLOW
    elif level == 2:
        return Fore.RED
    else:
        return Fore.RED + Style.BRIGHT

def print_log(log_entry, response_status, response_text):
    level = log_entry["level"]
    level_name = get_level_name(level)
    level_color = get_level_color(level)
    
    timestamp = datetime.now().strftime("%H:%M:%S")
    service = log_entry["serviceName"]
    message = log_entry["message"]
    
    print(f"{Fore.CYAN}[{timestamp}]{Style.RESET_ALL} ", end="")
    print(f"{level_color}[{level_name}]{Style.RESET_ALL} ", end="")
    print(f"{Fore.BLUE}{service}{Style.RESET_ALL} -> ", end="")
    print(f"{message} -> ", end="")
    
    if response_status == 200:
        print(f"{Fore.GREEN}[{response_status} OK]{Style.RESET_ALL}")
    elif response_status == 202:
        print(f"{Fore.GREEN}[{response_status} Accepted]{Style.RESET_ALL}")
    elif response_status == 429:
        print(f"{Fore.YELLOW}[{response_status} Too Many Requests]{Style.RESET_ALL}")
    elif response_status == 500:
        print(f"{Fore.RED}[{response_status} Internal Server Error]{Style.RESET_ALL}")
    else:
        print(f"{Fore.YELLOW}[{response_status}]{Style.RESET_ALL}")
    
    print()

def send_log(log_entry):
    try:
        response = requests.post(API_URL, json=log_entry, timeout=5)
        return response.status_code, response.text
    except requests.exceptions.Timeout:
        return 408, "Request timeout"
    except requests.exceptions.ConnectionError:
        return 503, "Service unavailable"
    except Exception as e:
        return 500, str(e)

def main():
    print(f"{Fore.MAGENTA}{'='*60}{Style.RESET_ALL}")
    print(f"{Fore.MAGENTA}AI Log Observability - Log Emulator{Style.RESET_ALL}")
    print(f"{Fore.MAGENTA}{'='*60}{Style.RESET_ALL}")
    print(f"{Fore.CYAN}Target API:{Style.RESET_ALL} {API_URL}")
    print(f"{Fore.CYAN}Services:{Style.RESET_ALL} {', '.join(SERVICES)}")
    print(f"{Fore.CYAN}Distribution:{Style.RESET_ALL} 60% Info, 20% Warning, 15% Error, 5% Critical (80% Info/Warning, 20% Error/Critical)")
    print(f"{Fore.YELLOW}Press Ctrl+C to stop{Style.RESET_ALL}")
    print(f"{Fore.MAGENTA}{'='*60}{Style.RESET_ALL}")
    print()
    
    log_count = 0
    
    try:
        while True:
            log_entry = generate_log_entry()
            status_code, response_text = send_log(log_entry)
            
            print_log(log_entry, status_code, response_text)
            
            log_count += 1
            if log_count % 10 == 0:
                print(f"{Fore.CYAN}Total logs sent: {log_count}{Style.RESET_ALL}")
                print()
            
            delay = random.uniform(0.5, 2.0)
            time.sleep(delay)
            
    except KeyboardInterrupt:
        print(f"\n{Fore.YELLOW}Emulator stopped by user{Style.RESET_ALL}")
        print(f"{Fore.CYAN}Total logs sent: {log_count}{Style.RESET_ALL}")
    except Exception as e:
        print(f"\n{Fore.RED}Error: {e}{Style.RESET_ALL}")

if __name__ == "__main__":
    main()
