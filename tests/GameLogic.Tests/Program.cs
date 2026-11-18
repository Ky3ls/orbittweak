// Checks of the "Andere Spiele" pure rules. Exit code 0 = all fine.
//   dotnet run --project tests/GameLogic.Tests
// Each game has its own file (FortniteTests.cs, Cs2Tests.cs, R6Tests.cs); IniTests.cs checks the shared INI helper.

int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL  " + what); } }

IniTests.Run(Check);
FortniteTests.Run(Check);
Cs2Tests.Run(Check);
R6Tests.Run(Check);

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
