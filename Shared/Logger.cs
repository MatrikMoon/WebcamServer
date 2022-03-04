﻿#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8604 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

/**
*Modified by Moon on ?/?/2018 (Originally taken from andruzzzhka's work)
 * Simple wrapper for Console.Log which makes logging
 * a little prettier
 */

namespace Shared
{
    public class Logger
    {
        //Added for the purpose of viewing log info in the UI
        public enum LogType
        {
            Error,
            Warning,
            Info,
            Success,
            Debug
        }

        public static event Action<LogType, string> MessageLogged;

        public static void Error(object message)
        {
            MessageLogged?.Invoke(LogType.Error, message.ToString());
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        public static void Warning(object message)
        {
            MessageLogged?.Invoke(LogType.Warning, message.ToString());
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        public static void Info(object message)
        {
            MessageLogged?.Invoke(LogType.Info, message.ToString());
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        public static void Success(object message)
        {
            MessageLogged?.Invoke(LogType.Success, message.ToString());
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        public static void Debug(object message)
        {
#if DEBUG
            MessageLogged?.Invoke(LogType.Debug, message.ToString());
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
#endif
        }

        public static void ColoredLog(object message, ConsoleColor color)
        {
            MessageLogged?.Invoke(LogType.Info, message.ToString());
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }
    }
}
