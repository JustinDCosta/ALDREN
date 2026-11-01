// Waits until it is killed; the Job Object test checks that closing the job ends it.
Console.WriteLine(Environment.ProcessId);
Thread.Sleep(Timeout.Infinite);
