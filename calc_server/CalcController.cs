using calc_server.models;
using log4net;
using Microsoft.AspNetCore.Mvc;

namespace calc_server;

[ApiController]
[Route("calculator")]
public class CalcController : ControllerBase
{
    private static readonly Stack<int> CalculatorStack = new();
    private static readonly List<HistoryEntry> History = new();
    private static readonly ILog StackLogger = LogManager.GetLogger("stack-logger");
    private static readonly ILog IndependentLogger = LogManager.GetLogger("independent-logger");

    private static readonly Dictionary<string, int> OperationArgumentCount = new()
    {
        ["plus"] = 2,
        ["minus"] = 2,
        ["times"] = 2,
        ["divide"] = 2,
        ["pow"] = 2,
        ["abs"] = 1,
        ["fact"] = 1
    };

    private static bool IsOperationValid(string? op, out int expectedArgCount)
    {
        if (!string.IsNullOrWhiteSpace(op) && OperationArgumentCount.TryGetValue(op, out expectedArgCount))
        {
            return true;
        }

        expectedArgCount = 0;
        return false;
    }


    private int PerformOperation(string op, List<int> args)
    {
        var x = args[0];
        var y = args.Count > 1 ? args[1] : 0;

        return op switch
        {
            "plus" => x + y,
            "minus" => x - y,
            "times" => x * y,
            "divide" => y == 0
                ? throw new InvalidOperationException("Error while performing operation Divide: division by 0")
                : x / y,
            "pow" => (int)Math.Pow(x, y),
            "abs" => Math.Abs(x),
            "fact" => x < 0
                ? throw new InvalidOperationException(
                    "Error while performing operation Factorial: not supported for the negative number")
                : Factorial(x),
            _ => throw new InvalidOperationException($"Error: unknown operation: {op}") // default case
        };
    }

    private int Factorial(int n)
    {
        var result = 1;

        for (var i = 2; i <= n; i++) result *= i;

        return result;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok("OK");
    }

    [HttpPost("independent/calculate")]
    public IActionResult IndependentCalculate([FromBody] CalcRequest request)
    {
        var op = request.operation?.ToLowerInvariant();
        var args = request.arguments;

        if (!IsOperationValid(op, out var expectedArgCount))
        {
            string errorMessage = $"Error: unknown operation: {request.operation}";
            IndependentLogger.Error($"Server encountered an error ! message: {errorMessage}");
            return Conflict
            (
                new CalcResponse
                {
                    errorMessage = $"Error: unknown operation: {request.operation}"
                }
            );
        }

        if (args == null || args.Count != expectedArgCount)
        {
            string errorMessage =
                $"Error: {(args == null || args.Count < expectedArgCount ? "Not enough" : "Too many")} " +
                $"arguments to perform the operation {request.operation}";
            IndependentLogger.Error($"Server encountered an error ! message: {errorMessage}");

            return Conflict
            (
                new CalcResponse
                {
                    errorMessage = errorMessage
                }
            );
        }

        try
        {
            var result = PerformOperation(op!, args);
            History.Add(new HistoryEntry
            {
                flavor = "INDEPENDENT",
                operation = request.operation!,
                arguments = args,
                result = result
            });

            IndependentLogger.Info($"Performing operation {request.operation}. Result is {result}");
            IndependentLogger.Debug($"Performing operation: {op}({String.Join(",", args)}) = {result}");
            return Ok(new CalcResponse { result = result });
        }
        catch (Exception ex)
        {
            IndependentLogger.Error($"Server encountered an error ! message: {ex.Message}");
            return Conflict
            (
                new CalcResponse
                {
                    errorMessage = ex.Message
                }
            );
        }
    }

    [HttpGet("stack/size")]
    public IActionResult StackSize()
    {
        StackLogger.Info($"Stack size is {CalculatorStack.Count}");
        StackLogger.Debug($"Stack content (first == top): [{string.Join(", ", CalculatorStack)}]");
        return Ok(new CalcResponse { result = CalculatorStack.Count });
    }

    [HttpPut("stack/arguments")]
    public IActionResult PushArgs([FromBody] CalcRequest request)
    {
        var args = request.arguments;
        int argsCount = args?.Count ?? 0;
        StackLogger.Info(
            $"Adding total of {argsCount} argument(s) to the stack | Stack size: {CalculatorStack.Count + argsCount}");
        StackLogger.Debug($"Adding arguments: {string.Join(",", args!)} | " +
                          $"Stack size before {CalculatorStack.Count} | stack size after {CalculatorStack.Count + argsCount}");

        if (args != null)
        {
            foreach (var arg in args)
            {
                CalculatorStack.Push(arg);
            }
        }

        return Ok
        (
            new CalcResponse
            {
                result = CalculatorStack.Count
            }
        );
    }

    [HttpGet("stack/operate")]
    public IActionResult StackOperate([FromQuery] string operation)
    {
        var op = operation.ToLower();
        if (!IsOperationValid(op, out var expectedArgCount))
        {
            string errorMessage = $"Error: unknown operation: {operation}";
            StackLogger.Error($"Server encountered an error ! message: {errorMessage}");
            return Conflict
            (
                new CalcResponse
                {
                    errorMessage = errorMessage
                }
            );
        }

        if (CalculatorStack.Count < expectedArgCount)
        {
            string errorMessage = $"Error: cannot implement operation {op}. " +
                                  $"It requires {expectedArgCount} arguments " +
                                  $"and the stack has only {CalculatorStack.Count} arguments";
            StackLogger.Error($"Server encountered an error ! message: {errorMessage}");
            return Conflict
            (
                new CalcResponse
                {
                    errorMessage = $"Error: cannot implement operation {op}. " +
                                   $"It requires {expectedArgCount} arguments " +
                                   $"and the stack has only {CalculatorStack.Count} arguments"
                }
            );
        }

        List<int> args = new() { CalculatorStack.Pop() };
        if (expectedArgCount > 1) args.Add(CalculatorStack.Pop());
        try
        {
            var result = PerformOperation(op, args);
            History.Add(new HistoryEntry
            {
                flavor = "STACK",
                operation = operation,
                arguments = args,
                result = result
            });
            StackLogger.Info($"Performing operation {op}. Result is {result} | stack size: {CalculatorStack.Count}");
            StackLogger.Debug($"Performing operation: {op}({String.Join(",", args)}) = {result}");
            return Ok(new CalcResponse { result = result });
        }
        catch (Exception ex)
        {
            StackLogger.Error($"Server encountered an error ! message: {ex.Message}");
            return Conflict
            (
                new CalcResponse
                {
                    errorMessage = ex.Message
                }
            );
        }
    }

    [HttpDelete("stack/arguments")]
    public IActionResult PopArgs([FromQuery] int count)
    {
        if (count > 0)
        {
            if (count > CalculatorStack.Count)
            {
                string errorMessage =
                    $"Error: cannot remove {count} from the stack. It has only {CalculatorStack.Count} arguments";
                StackLogger.Error($"Server encountered an error ! message: {errorMessage}");
                return Conflict
                (
                    new CalcResponse
                    {
                        errorMessage = errorMessage
                    }
                );
            }

            for (var i = 0; i < count; i++)
            {
                CalculatorStack.Pop();
            }

            StackLogger.Info(
                $"Removing total {count} argument(s) from the stack | Stack size: {CalculatorStack.Count}");
        }

        return Ok(new CalcResponse { result = CalculatorStack.Count });
    }

    [HttpGet("history")]
    public IActionResult GetHistory([FromQuery] string? flavor)
    {
        List<HistoryEntry> entries;

        if (string.IsNullOrEmpty(flavor))
        {
            // No filter — return STACK first, then INDEPENDENT
            entries = History.FindAll(entry => entry.flavor == "STACK");
            entries.AddRange(History.FindAll(entry => entry.flavor == "INDEPENDENT"));
            StackLogger.Info($"History: So far total {entries.Count(entry => entry.flavor == "STACK")} stack actions");
            IndependentLogger.Info(
                $"History: So far total {entries.Count(entry => entry.flavor == "INDEPENDENT")} independent actions");
        }
        else if (flavor == "STACK")
        {
            entries = History.FindAll(entry => entry.flavor == "STACK");
            StackLogger.Info($"History: So far total {entries.Count(entry => entry.flavor == "STACK")} stack actions");
        }
        else
        {
            entries = History.FindAll(entry => entry.flavor == "INDEPENDENT");
            IndependentLogger.Info(
                $"History: So far total {entries.Count(entry => entry.flavor == "INDEPENDENT")} independent actions");
        }

        return Ok(new { result = entries });
    }
}