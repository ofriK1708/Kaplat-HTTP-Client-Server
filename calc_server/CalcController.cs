using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using calc_server.models;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace calc_server;

[ApiController]
[Route("calculator")]
public class CalcController : ControllerBase
{
    private static readonly Stack<int> CalculatorStack = new();
    private static readonly List<HistoryEntry> History = new();
    private static readonly ILog StackLogger = LogManager.GetLogger("stack-logger");
    private static readonly ILog IndependentLogger = LogManager.GetLogger("independent-logger");
    private readonly CalculatorDbContext _postgresContext;
    private readonly IMongoCollection<OperationEntry> _mongoCollection;

    public CalcController(CalculatorDbContext postgresContext, IMongoClient mongoClient)
    {
        _postgresContext = postgresContext;
        // Connect to the specific DB and Collection for Mongo
        var database = mongoClient.GetDatabase("calculator");
        _mongoCollection = database.GetCollection<OperationEntry>("calculator");
    }

    private async Task SaveToDatabases(HistoryEntry entry)
    {
        // create the entry object
        var dbEntry = OperationEntry.FromHistoryEntry(entry);
        
        // Manually generate ID
        var maxId = await _postgresContext.Operations.MaxAsync(o => (int?)o.rawid) ?? 0;
        dbEntry.rawid = maxId + 1;

        try
        {
            // save to Postgres
            _postgresContext.Operations.Add(dbEntry);
            await _postgresContext.SaveChangesAsync();
            
            // save to mongo
            await _mongoCollection.InsertOneAsync(dbEntry);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            IndependentLogger.Error($"SaveChanges failed: {ex.Message}");
            IndependentLogger.Error($"Base exception: {ex.GetBaseException()?.Message}");
            if (ex.InnerException != null) IndependentLogger.Error($"Inner exception: {ex.InnerException.Message}");

            throw; // rethrow or return a proper API response (400/500) after logging
        }
    }

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
    public async Task<IActionResult> IndependentCalculate([FromBody] CalcRequest request)
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
            IndependentLogger.Info($"Performing operation {request.operation}. Result is {result}");
            IndependentLogger.Debug($"Performing operation: {op}({String.Join(",", args)}) = {result}");
            HistoryEntry entry = new HistoryEntry
            {
                flavor = HistoryEntry.INDEPENDENT_FLAVOR,
                operation = request.operation!,
                arguments = args,
                result = result
            };

            History.Add(entry);
            await SaveToDatabases(entry);


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
    public async Task<IActionResult> StackOperate([FromQuery] string operation)
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
            StackLogger.Info($"Performing operation {op}. Result is {result} | stack size: {CalculatorStack.Count}");
            StackLogger.Debug($"Performing operation: {op}({String.Join(",", args)}) = {result}");
            var entry = new HistoryEntry
            {
                flavor = HistoryEntry.STACK_FLAVOR,
                operation = operation,
                arguments = args,
                result = result
            };
            History.Add(entry);
            await SaveToDatabases(entry);
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
    public async Task<IActionResult> GetHistory([FromQuery] string? flavor, [FromQuery, Required] string? 
        persistenceMethod)
    {
        List<OperationEntry> entries;
        if (persistenceMethod != HistoryEntry.PERSISTNECE_MONGO &&
            persistenceMethod != HistoryEntry.PERSISTENCE_POSTGRES)
        {
            string errorMessage =
                $"Error: unknown persistence method";
            return Conflict
            (
                new CalcResponse
                {
                    errorMessage = errorMessage
                }
            );
        }
        if (persistenceMethod == HistoryEntry.PERSISTNECE_MONGO)
        {
            entries = await FetchFromMongo(flavor);
        }
        else
        {
            entries = await FetchFromPostgres(flavor);
        }

        return Ok(new { result = entries });
    }

    private async Task<List<OperationEntry>> FetchFromPostgres(string? flavor)
    {
        List<OperationEntry> entries;
        if (String.IsNullOrEmpty(flavor))
        {
            entries = await _postgresContext.Operations
                .Where(ope => ope.flavor == HistoryEntry.INDEPENDENT_FLAVOR)
                .ToListAsync();

            entries.AddRange(await _postgresContext.Operations
                .Where(ope => ope.flavor == HistoryEntry.STACK_FLAVOR)
                .ToListAsync());
        }
        else
        {
            entries = await _postgresContext.Operations
                .Where(e => e.flavor == flavor)
                .ToListAsync();
        }

        return entries;
    }

    private async Task<List<OperationEntry>> FetchFromMongo(string? flavor)
    {
        List<OperationEntry> entries;
        var independentFlavorFilter = Builders<OperationEntry>.Filter.Eq(ope => ope.flavor, HistoryEntry
            .INDEPENDENT_FLAVOR);
        var stackFlavorFilter = Builders<OperationEntry>.Filter.Eq(poe => poe.flavor, HistoryEntry.STACK_FLAVOR);
        if (String.IsNullOrEmpty(flavor))
        {
            // No filter — return STACK first, then INDEPENDENT
            var independentCursor = await _mongoCollection.FindAsync(independentFlavorFilter);
            var stackCursor = await _mongoCollection.FindAsync(stackFlavorFilter);
            entries = await independentCursor.ToListAsync();
            entries.AddRange(await stackCursor.ToListAsync());
        }
        else
        {
            var cursor = flavor == HistoryEntry.INDEPENDENT_FLAVOR
                ? await _mongoCollection.FindAsync(independentFlavorFilter)
                : await _mongoCollection.FindAsync(stackFlavorFilter);
            entries = await cursor.ToListAsync();
        }
        return  entries;
    }
}