﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;

public static class YieldReturn
{
	public static IEnumerable<string> SimpleYieldReturn()
	{
		yield return "A";
		yield return "B";
		yield return "C";
	}
	
	public static IEnumerable<int> YieldReturnInLoop()
	{
		for (int i = 0; i < 100; i++) {
			yield return i;
		}
	}
	
	public static IEnumerable<int> YieldReturnWithTryFinally()
	{
		yield return 0;
		try {
			yield return 1;
		} finally {
			Console.WriteLine("Finally!");
		}
		yield return 2;
	}
	
	
	public static IEnumerable<string> YieldReturnWithNestedTryFinally(bool breakInMiddle)
	{
		Console.WriteLine("Start of method - 1");
		yield return "Start of method";
		Console.WriteLine("Start of method - 2");
		try {
			Console.WriteLine("Within outer try - 1");
			yield return "Within outer try";
			Console.WriteLine("Within outer try - 2");
			try {
				Console.WriteLine("Within inner try - 1");
				yield return "Within inner try";
				Console.WriteLine("Within inner try - 2");
				if (breakInMiddle)
					yield break;
				Console.WriteLine("End of inner try - 1");
				yield return "End of inner try";
				Console.WriteLine("End of inner try - 2");
			} finally {
				Console.WriteLine("Inner Finally");
			}
			Console.WriteLine("End of outer try - 1");
			yield return "End of outer try";
			Console.WriteLine("End of outer try - 2");
		} finally {
			Console.WriteLine("Outer Finally");
		}
		Console.WriteLine("End of method - 1");
		yield return "End of method";
		Console.WriteLine("End of method - 2");
	}
	
	public static IEnumerable<string> YieldReturnWithTwoNonNestedFinallyBlocks(IEnumerable<string> input)
	{
		// outer try-finally block
		foreach (string line in input) {
			// nested try-finally block
			try {
				yield return line;
			} finally {
				Console.WriteLine("Processed " + line);
			}
		}
		yield return "A";
		yield return "B";
		yield return "C";
		yield return "D";
		yield return "E";
		yield return "F";
		// outer try-finally block
		foreach (string line in input)
			yield return line.ToUpper();
	}
	
	public static IEnumerable<Func<string>> YieldReturnWithAnonymousMethods1(IEnumerable<string> input)
	{
		foreach (string line in input) {
			yield return () => line;
		}
	}
	
	public static IEnumerable<Func<string>> YieldReturnWithAnonymousMethods2(IEnumerable<string> input)
	{
		foreach (string line in input) {
			string copy = line;
			yield return () => copy;
		}
	}
	
	public static IEnumerable<int> GetEvenNumbers(int n)
	{
		for (int i = 0; i < n; i++) {
			if (i % 2 == 0)
				yield return i;
		}
	}
}
