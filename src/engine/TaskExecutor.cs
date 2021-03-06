﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

/// <summary>
///   Manages running a reasonable number of parallel tasks at once
/// </summary>
public class TaskExecutor
{
    private static readonly TaskExecutor INSTANCE = new TaskExecutor();

    private readonly BlockingCollection<ThreadCommand> queuedTasks =
        new BlockingCollection<ThreadCommand>();

    private bool running = true;
    private int currentThreadCount = 0;
    private bool assumeHyperThreading = true;

    static TaskExecutor()
    {
    }

    private TaskExecutor(int overrideParallelCount = -1)
    {
        if (overrideParallelCount >= 0)
        {
            ParallelTasks = overrideParallelCount;
        }
        else
        {
            var logicalCPUs = System.Environment.ProcessorCount;

            int targetTaskCount = logicalCPUs;

            // No platform independent way to get this in c#
            if (assumeHyperThreading && logicalCPUs % 2 == 0)
                targetTaskCount /= 2;

            // Actually it might make more sense to run auto evo as tasks to
            // only take up resources when it is running
            // // One thread for auto-evo
            // targetTaskCount -= 1;

            // There needs to be 2 threads as when auto-evo is running it hogs one thread
            if (targetTaskCount < 2)
            {
                ParallelTasks = 2;
            }
            else
            {
                ParallelTasks = targetTaskCount;
            }
        }

        GD.Print("TaskExecutor started with parallel job count: ", ParallelTasks);
    }

    public static TaskExecutor Instance
    {
        get
        {
            return INSTANCE;
        }
    }

    public int ParallelTasks
    {
        get
        {
            return currentThreadCount;
        }
        set
        {
            if (currentThreadCount == value)
                return;

            while (currentThreadCount > value)
            {
                QuitThread();
            }

            while (currentThreadCount < value)
            {
                SpawnThread();
            }
        }
    }

    /// <summary>
    ///   Sends a new task to be executed
    /// </summary>
    public void AddTask(Task task)
    {
        if (task != null)
        {
            queuedTasks.Add(new ThreadCommand(ThreadCommand.Type.Task, task));
        }
    }

    /// <summary>
    ///   Runs a list of tasks and waits for them to complete. The
    ///   first task is ran on the calling thread before waiting.
    /// </summary>
    public void RunTasks(IEnumerable<Task> tasks)
    {
        // Queue all but the first task
        Task firstTask = null;

        foreach (var task in tasks)
        {
            if (firstTask != null)
            {
                AddTask(task);
            }
            else
            {
                firstTask = task;
            }
        }

        // Run the first task on this thread
        if (firstTask != null)
            firstTask.RunSynchronously();

        // Wait for all tasks to complete
        foreach (var task in tasks)
        {
            task.Wait();
        }
    }

    public void Quit()
    {
        running = false;
        ParallelTasks = 0;
    }

    private void SpawnThread()
    {
        var thread = new System.Threading.Thread(RunExecutorThread);
        thread.IsBackground = true;
        thread.Start();
        ++currentThreadCount;
    }

    private void QuitThread()
    {
        if (currentThreadCount <= 0)
            return;

        queuedTasks.Add(new ThreadCommand(ThreadCommand.Type.Quit, null));

        --currentThreadCount;
    }

    private void RunExecutorThread()
    {
        while (running)
        {
            if (queuedTasks.TryTake(out ThreadCommand command, 30000))
            {
                if (command.CommandType == ThreadCommand.Type.Quit)
                {
                    return;
                }
                else if (command.CommandType == ThreadCommand.Type.Task)
                {
                    command.Task.RunSynchronously();

                    // Make sure task exceptions aren't ignored.
                    // Could perhaps in the future find some othre way to handle this
                    if (command.Task.Exception != null)
                        throw command.Task.Exception;
                }
                else
                {
                    throw new Exception("invalid task type");
                }
            }
        }
    }

    private struct ThreadCommand
    {
        public Type CommandType;
        public Task Task;

        public ThreadCommand(Type commandType, Task task)
        {
            CommandType = commandType;
            Task = task;
        }

        public enum Type
        {
            Task,
            Quit,
        }
    }
}
