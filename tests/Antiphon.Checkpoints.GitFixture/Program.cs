// Only the two data queries exercised by the encoding witness emit invalid UTF-8.
var operands = args.AsSpan();
while (operands.Length >= 2 && operands[0] == "-c") operands = operands[2..];
if (operands.Length == 0) return 91;
var command = operands[0];
if (command is not ("config" or "status" or "ls-files")) return 91;
var log = Environment.GetEnvironmentVariable("C1608_GIT_LOG");
if (!string.IsNullOrEmpty(log)) File.AppendAllText(log, command + Environment.NewLine);
if (command != "config") Console.OpenStandardOutput().WriteByte(255);
return 0;
