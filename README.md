[README.md](https://github.com/vestaabner/Channel/files/6480209/README.md)
# ChannelIn this article, we’ll explore the synchronization data structures in .NET’s System.Threading.Channels namespace and learn how to use them for designing concurrent workflows. It would be helpful to have some basic understanding of .NET’s Task Parallel Library (TPL), but it’s in no means necessary.

Recently, I watched Rob Pike’s talk on “Go Concurrency Patterns” where he explains Go’s approach to concurrency and demonstrates some of its features for building concurrent programs. I found its simplicity and ease of use fascinating and went on to implement some of these techniques in C#. Let’s start by introducing some definitions.

Concurrency
The relationship between concurrency and parallelism is commonly misunderstood. In fact, two procedures being concurrent doesn’t mean that they’ll run in parallel. The following quote by Martin Kleppmann has stood out in my mind when it comes to concurrency:

For defining concurrency, the exact time doesn’t matter: we simply call two operations concurrent if they are both unaware of each other, regardless of the physical time at which they occurred.

– “Designing Data-Intensive Applications” by Martin Kleppmann

Concurrency is something that enables parallelism. On a single processor, two procedures can be concurrent, yet they won’t run in parallel. A concurrent program deals with a lot of things at once, whereas a parallel program does a lot of things at once.
Think of it this way - concurrency is about structure and parallelism is about execution. A concurrent program may benefit from parallelism, but that’s not its goal. The goal of concurrency is a good structure.

Channels
A concurrent program is structured into independent pieces that we have to coordinate. To make that work, we need some form of communication. There’re several ways to achieve that in .NET. In this article, we’ll explore the System.Threading.Channels (ships with .NET Core 3.1 SDK or available as a NuGet package) which provides an API, analogous to Go’s built-in channel primitive.


A channel is a data structure that allows one thread to communicate with another thread. In .NET, this was usually done by using a shared variable that supports concurrency (by implementing some synchronization/locking mechanism). Channels, on the other hand, can be used to send messages directly between threads without any external synchronization or locking required. These messages are sent in FIFO (first in first out) order. Here’s how we create a channel:

Channel<string> ch = Channel.CreateUnbounded<string>();
Channel is a static class that exposes several factory methods for creating channels. Channel<T> is a data structure that supports reading and writing. That’s how we write asynchronously to a channel:

await ch.Writer.WriteAsync("My first message");
await ch.Writer.WriteAsync("My second message");
ch.Writer.Complete();
This is how we read from a channel:

while (await ch.Reader.WaitToReadAsync()) 
    Console.WriteLine(await ch.Reader.ReadAsync());
The reader’s WaitToReadAsync() will complete with true when data is available to read, or with false when no further data will ever be read, that is, after the writer invokes Complete(). The reader also provides an option to consume the data as an async stream by exposing a method that returns IAsyncEnumerable<T>:

await foreach (var item in ch.Reader.ReadAllAsync())
    Console.WriteLine(item);
Channels also have a blocking API which we won’t cover in this article.

Using Channels
Here’s a basic example when we have a separate producer and consumer threads which communicate through a channel.

var ch = Channel.CreateUnbounded<string>();

var consumer = Task.Run(async () =>
{
    while (await ch.Reader.WaitToReadAsync())
        Console.WriteLine(await ch.Reader.ReadAsync());
});
var producer = Task.Run(async () =>
{
    var rnd = new Random();
    for (int i = 0; i < 5; i++)
    {
        await Task.Delay(TimeSpan.FromSeconds(rnd.Next(3)));
        await ch.Writer.WriteAsync($"Message {i}");
    }
    ch.Writer.Complete();
});

await Task.WhenAll(producer, consumer);
[12:27:16 PM] Message 0
[12:27:18 PM] Message 1
[12:27:19 PM] Message 2
[12:27:20 PM] Message 3
[12:27:22 PM] Message 4
The consumer (reader) waits until there’s an available message to read. On the other side, the producer (writer) waits until it’s able to send a message, hence, we say that channels both communicate and synchronize. Both operations are non-blocking, that is, while we wait, the thread is free to do some other work.
Notice that we have created an unbounded channel, meaning that it accepts as many messages as it can with regards to the available memory. With bounded channels, however, we can limit the number of messages that can be processed at a time.

var ch = Channel.CreateBounded<string>(capacity: 10);
So when this limit is reached, WriteAsync() won’t be able to write, until there’s an available slot in the channel’s buffer. A slot is freed up when a consumer reads from the channel.

Concurrency Patterns
Don’t communicate by sharing memory, share memory by communicating.

It’s time to explore a few concurrent programming techniques for working with channels. This part consists of several examples that are independent of each other. You can also find the interactive version of them on GitHub.

The Generator
A generator is a method that returns a channel. The one below creates a channel and writes a given number of messages asynchronously from a separate thread.

static ChannelReader<string> CreateMessenger(string msg, int count)
{
    var ch = Channel.CreateUnbounded<string>();
    var rnd = new Random();

    Task.Run(async () =>
    {
        for (int i = 0; i < count; i++)
        {
            await ch.Writer.WriteAsync($"{msg} {i}");
            await Task.Delay(TimeSpan.FromSeconds(rnd.Next(3)));
        }
        ch.Writer.Complete();
    });

    return ch.Reader;
}
By returning a ChannelReader<T> we ensure that our consumers won’t be able to attempt writing to it.

var joe = CreateMessenger("Joe", 5);
await foreach (var item in joe.ReadAllAsync())
    Console.WriteLine(item);
[7:31:39 AM] Joe 0
[7:31:40 AM] Joe 1
[7:31:42 AM] Joe 2
[7:31:44 AM] Joe 3
[7:31:44 AM] Joe 4
Let’s try to read from multiple channels.

var joe = CreateMessenger("Joe", 3);
var ann = CreateMessenger("Ann", 3);

while (await joe.WaitToReadAsync() || await ann.WaitToReadAsync())
{
    Console.WriteLine(await joe.ReadAsync());
    Console.WriteLine(await ann.ReadAsync());
}
[8:00:51 AM] Joe 0
[8:00:51 AM] Ann 0
[8:00:52 AM] Joe 1
[8:00:52 AM] Ann 1
[8:00:54 AM] Joe 2
[8:00:54 AM] Ann 2
This approach is problematic in several ways. Suppose Ann sends more messages than Joe:

var joe = CreateMessenger("Joe", 2);
var ann = CreateMessenger("Ann", 5);
We’re still going to try and read from Joe, even when his channel is completed which is going to throw an exception.

[8:05:01 AM] Joe 0
[8:05:01 AM] Ann 0
[8:05:02 AM] Joe 1
[8:05:02 AM] Ann 1
Unhandled exception. System.Threading.Channels.ChannelClosedException: 
    The channel has been closed.
A quick and dirty solution would be to wrap it in a try/catch block:

try
{
    Console.WriteLine(await joe.ReadAsync());
    Console.WriteLine(await ann.ReadAsync());
}
catch (ChannelClosedException) { }
Our code is concurrent, but not optimal because it executes in a lockstep. Suppose, Ann is more talkative than Joe, so her messages have an up to 3 seconds delay, whereas Joe sends messages on up to every 10 seconds. This will force us to wait for Joe, even though we might have several messages waiting ready to be read from Ann. Currently, we cannot read from Ann before reading from Joe. We should be doing better!

Multiplexer
We want to read from both Joe and Ann and process whoever’s message arrives first. We’re going to solve this by consolidating their messages into a single channel. Let’s define our Merge<T>() method:

static ChannelReader<T> Merge<T>(
    ChannelReader<T> first, ChannelReader<T> second)
{
    var output = Channel.CreateUnbounded<T>();

    Task.Run(async () =>
    {
        await foreach (var item in first.ReadAllAsync())
            await output.Writer.WriteAsync(item);
    });
    Task.Run(async () =>
    {
        await foreach (var item in second.ReadAllAsync())
            await output.Writer.WriteAsync(item);
    });

    return output;
}
Merge<T>() takes two channels and starts reading from them simultaneously. It creates and immediately returns a new channel which consolidates the outputs from the input channels. The reading procedures are run asynchronously on separate threads. Think of it like this:



That’s how we use it.

var ch = Merge(CreateMessenger("Joe", 3), CreateMessenger("Ann", 5));

await foreach (var item in ch.ReadAllAsync())
    Console.WriteLine(item);
[8:39:32 AM] Ann 0
[8:39:32 AM] Joe 0
[8:39:32 AM] Ann 1
[8:39:33 AM] Ann 2
[8:39:33 AM] Ann 3
[8:39:34 AM] Joe 1
[8:39:35 AM] Ann 4
[8:39:36 AM] Joe 2
We have simplified our code and solved the problem with Ann sending more messages than Joe while also doing it more often. However, you might have noticed that Merge<T>() has a defect - the output channel’s writer never closes which leads us to continue waiting to read, even when Joe and Ann have finished sending messages. Also, there’s no way for us to handle a potential failure of any of the readers. We have to modify our code:

static ChannelReader<T> Merge<T>(params ChannelReader<T>[] inputs)
{
    var output = Channel.CreateUnbounded<T>();

    Task.Run(async () =>
    {
        async Task Redirect(ChannelReader<T> input)
        {
            await foreach (var item in input.ReadAllAsync())
                await output.Writer.WriteAsync(item);
    	}
            
        await Task.WhenAll(inputs.Select(i => Redirect(i)).ToArray());
        output.Writer.Complete();
    });

    return output;
}
Our Merge<T>() now also works with an arbitrary number of inputs.
We’ve created the local asynchronous Redirect() function which takes a channel as an input writes its messages to the consolidated output. It returns a Task so we can use WhenAll() to wait for the input channels to complete. This allows us to also capture potential exceptions. In the end, we know that there’s nothing left to be read, so we can safely close the writer.

Our code is concurrent and non-blocking. The messages are being processed at the time of arrival and there’s no need to use locks or any kind of conditional logic. While we’re waiting, the thread is free to perform other work. We also don’t have to handle the case when one of the writers complete (as you can see Ann has sent all of her messages before Joe).

Demultiplexer
Joe talks too much and we cannot handle all of his messages. We want to distribute the work amongst several consumers. Let’s define Split<T>():



static IList<ChannelReader<T>> Split<T>(ChannelReader<T> ch, int n)
{
    var outputs = new Channel<T>[n];

    for (int i = 0; i < n; i++)
        outputs[i] = Channel.CreateUnbounded<T>();

    Task.Run(async () =>
    {
        var index = 0;
        await foreach (var item in ch.ReadAllAsync())
        {
            await outputs[index].Writer.WriteAsync(item);
            index = (index + 1) % n;
        }

        foreach (var ch in outputs)
            ch.Writer.Complete();
    });

    return outputs.Select(ch => ch.Reader).ToArray();
}
Split<T> takes a channel and redirects its messages to n number of newly created channels in a round-robin fashion. Here’s how to use it:

var joe = CreateMessenger("Joe", 10);
var readers = Split<int>(joe, 3);
var tasks = new List<Task>();

for (int i = 0; i < readers.Count; i++)
{
    var reader = readers[i];
    var index = i;
    tasks.Add(Task.Run(async () =>
    {
        await foreach (var item in reader.ReadAllAsync())
            Console.WriteLine($"Reader {index}: {item}");
    }));
}

await Task.WhenAll(tasks);
Joe sends 10 messages which we distribute amongst 3 channels. Below is a possible output we can get. Some channels may take longer to process a message, therefore, we have no guarantee that the order of emission is going to be preserved. Our code is structured so that we process (in this case log) a message as soon as it arrives.

Reader 0: Joe 0
Reader 1: Joe 1
Reader 0: Joe 3
Reader 2: Joe 2
Reader 1: Joe 4
Reader 2: Joe 5
Reader 0: Joe 6
Reader 1: Joe 7
Reader 2: Joe 8
Reader 0: Joe 9
Conclusion
In this article, we defined the term concurrency and discussed how it relates to parallelism. We explained why the two terms should not be confused. Then we explored C#’s channel data structure and learned how to use it to implement publish/subscribe workflows. We’ve seen how to make an efficient use multiple CPUs by distrubuting the reading/writing operations amongst several workers.

Check out part 2 where we discuss some cancellation techniques and part 3 where we put what we’ve learned into practice.
C# Channels - Timeout and Cancellation
This is a continuation of the article on how to build publish/subscribe workflows in C# where we learned how to use channels in C#. We also went through some techniques about distributing computations among several workers to make use of the modern-day, multi-core CPUs.

In this article, we’ll build on top of that by exploring some cases where we have to “exit” a channel. In the last example, we’ll put what we’ve covered so far in practice. You can check out the interactive version of the demos on GitHub.

Timeout
We want to stop reading from a channel after a certain amount of time. This is quite simple because the channels' async API supports cancellation.



var joe = CreateMessenger("Joe", 10);
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    await foreach (var item in joe.ReadAllAsync(cts.Token))
        Console.WriteLine(item);

    Console.WriteLine("Joe sent all of his messages."); 
}
catch (OperationCanceledException)
{
    Console.WriteLine("Joe, you are too slow!");
}
Joe 0
Joe 1
Joe 2
Joe 3
Joe, you are too slow!
Joe was set to send 10 messages but over 5 seconds, we received only 4 and then canceled the reading operation. If we reduced the number of messages Joe sends or sufficiently increased the timeout duration, we’d read everything and thus avoid ending up in the catch block.

Quit Channel
In this article, we’ll learn how to efficiently process data in a non-blocking way using the pipeline pattern. We’ll construct composable and testable pipelines using .NET’s channels, and see how to perform cancellation and deal with errors. If you’re new to the concept of channels, I suggest checking out part 1 and part 2 of the series first.

Pipelines
A pipeline is a concurrency model where a job is handled through several processing stages. Each stage performs a part of the full job and when it’s done, it forwards it to the next stage. It also runs in a separate thread and shares no state with the other stages.



The generator delegates the jobs, which are being processed through the pipeline. For example, we can represent the pizza preparation as a pipeline, consisting of the following stages:

Pizza order (Generator)
Prepare dough
Add toppings
Bake in oven
Put in a box
Stages start executing as soon as their input is ready, that is, when stage 2 adds toppings to pizza 1, stage 1 can prepare the dough for pizza 2, when stage 4 puts the baked pizza 1 in a box, stage 1 might be preparing the dough for pizza 3 and so on.

Implementing a Channel-based Pipeline
Each pipeline starts with a generator method, which initiates jobs by passing it to the stages. The intermediate stages are also methods that run concurrently. Channels serve as a transport mechanism between the stages. A stage takes a channel as an input, performs some work on each data item it asynchronously reads, and passes the result to an output channel. The purpose of a stage is to do one job and do it well.



To see it in action, we’re going to implement a program that efficiently counts the lines of code in a project.

The Generator - Enumerate the Files
The initial stage of our pipeline would be to enumerate recursively the files in the workspace. We implement it as a generator.

ChannelReader<string> GetFilesRecursively(string root)
{
    var output = Channel.CreateUnbounded<string>();
    
    async Task WalkDir(string path)
    {
        foreach (var file in Directory.GetFiles(path))
            await output.Writer.WriteAsync(file);

        var tasks = Directory.GetDirectories(path).Select(WalkDir);
        await Task.WhenAll(tasks.ToArray());
    }

    Task.Run(async () =>
    {
        await WalkDir(root);
        output.Writer.Complete();
    });

    return output;
}
We perform a depth-first traversal of the directory and its subdirectories and write each file name we encounter to the output channel. When we’re done with the traversal, we mark the channel as complete so the consumer (the next stage) knows when to stop reading from it.

Stage 1 - Keep the Source Files
Stage 1 is going to determine whether the file contains source code or not. The ones that don’t should be discarded.

ChannelReader<FileInfo> FilterByExtension(
    ChannelReader<string> input, HashSet<string> exts)
{
    var output = Channel.CreateUnbounded<FileInfo>();
    
    Task.Run(async () =>
    {
        await foreach (var file in input.ReadAllAsync())
        {
            var fileInfo = new FileInfo(file);
            if (exts.Contains(fileInfo.Extension))
                await output.Writer.WriteAsync(fileInfo);
        }
        output.Writer.Complete();
    });

    return output;
}
This stage takes an input channel (produced by the generator) from which it asynchronously reads the file names. For each file, it gets its metadata and checks whether the extension belongs to the set of the source code file extensions. It also transforms the input, that is, for each file that satisfies the condition, it writes a FileInfo object to the output channel.

Stage 2 - Get the Line Count
This stage is responsible for counting the number of lines in each file.

ChannelReader<(FileInfo file, int lines)> 
    GetLineCount(ChannelReader<FileInfo> input)
{
    var output = Channel.CreateUnbounded<(FileInfo, int)>();

    Task.Run(async () =>
    {
        await foreach (var file in input.ReadAllAsync())
        {
            var lines = CountLines(file);
            await output.Writer.WriteAsync((file, lines));
        }
        output.Writer.Complete();
    });

    return output;
}
We write a tuple of type (FileInfo, int) which is a pair of the file metadata and its number of lines. The int CountLines(FileInfo file) method is straightforward, you can check out the implementation below.

Expand CountLines
The Sink Stage
Now we’ve implemented the stages of our pipeline, we are ready to put them all together.

var fileGen = GetFilesRecursively("path_to/node_modules");
var sourceCodeFiles = FilterByExtension(
    fileGen, new HashSet<string> { ".js", ".ts" });
var counter = GetLineCount(sourceCodeFiles);
The sink stage processes the output from the last stage of the pipeline. Unlike the generator, which doesn’t consume, but only produces, the sink only consumes but doesn’t produce. That’s where our pipeline comes to an end.

var totalLines = 0;
await foreach (var item in counter.ReadAllAsync())
{
    Console.WriteLine($"{item.file.FullName} {item.lines}");
    totalLines += item.lines;
}

Console.WriteLine($"Total lines: {totalLines}");
/Users/denis/Workspace/proj/index.js 155
/Users/denis/Workspace/proj/main.ts 97
/Users/denis/Workspace/proj/tree.ts 0
/Users/denis/Workspace/proj/lib/index.ts 210
Total lines: 462
Error Handling
We’ve covered the happy path, however, our pipeline might encounter malformed or erroneous inputs. Each stage has its own notion of what an invalid input is, so it’s its own responsibility to deal with it. To achieve good error handling, our pipeline needs to satisfy the following:

Invalid input should not propagate to the next stage
Invalid input should not cause the pipeline to stop. The pipeline should continue to process the inputs thereafter.
The pipeline should not swallow errors. All the errors should be reported.
We’re going to modify stage 2 which counts the lines in a file. Our definition for invalid input is an empty file. Our pipeline should not pass them forward and instead, it should report the existence of such files. We solve that by introducing a second channel that emits the errors.

- ChannelReader<(FileInfo file, int lines)> GetLineCount(
+     (ChannelReader<(FileInfo file, int lines)> output,
+      ChannelReader<string> errors)
    GetLineCount(ChannelReader<FileInfo> input)
    {
        var output = Channel.CreateUnbounded<(FileInfo, int)>();
+       var errors = Channel.CreateUnbounded<string>();

        Task.Run(async () =>
        {
            await foreach (var file in input.ReadAllAsync())
            {
                var lines = CountLines(file);
-               await output.Writer.WriteAsync((file, lines));
+               if (lines == 0)
+                   await errors.Writer.WriteAsync(
+                       $"[Error] Empty file {file}");
+               else
+                   await output.Writer.WriteAsync((file, lines));
            }
            output.Writer.Complete();
+           errors.Writer.Complete();
        });
-       return output;
+       return (output, errors);
    }
We’ve created a second channel for errors and changed the signature of the method so it returns both the output and the error channels. Empty files are not passed to the next stage, we’ve provided a mechanism to report them using the error channel and after we get an invalid input, our pipeline continues processing the next ones.

var fileGen = GetFilesRecursively("path_to/node_modules");
var sourceCodeFiles = FilterByExtension(
    fileGen, new HashSet<string> { ".js", ".ts" });
var (counter, errors) = GetLineCount(sourceCodeFiles);
var totalLines = 0;

await foreach (var item in counter.ReadAllAsync())
    totalLines += item.lines;

Console.WriteLine($"Total lines: {totalLines}");

await foreach (var errMessage in errors.ReadAllAsync())
    Console.WriteLine(errMessage);
/Users/denis/Workspace/proj/index.js 155
/Users/denis/Workspace/proj/main.ts 97
/Users/denis/Workspace/proj/lib/index.ts 210
Total lines: 462
[Error] Empty file /Users/denis/Workspace/proj/tree.ts
Cancellation
Similarily to the error handling, the stages being independent means that each has to handle cancellation on their own. To stop the pipeline, we need to prevent the generator from delegating new jobs. Let’s make it cancellable.

ChannelReader<string> GetFilesRecursively(
    string root, CancellationToken token = default)
{
    var output = Channel.CreateUnbounded<string>();

    async Task WalkDir(string path)
    {
        if (token.IsCancellationRequested)
            throw new OperationCanceledException();
            
        foreach (var file in Directory.GetFiles(path))
            await output.Writer.WriteAsync(file, token);
            
        var tasks = Directory.GetDirectories(path).Select(WalkDir);
        await Task.WhenAll(tasks.ToArray()));
    }

    Task.Run(async () =>
    {
        try
        {
            await WalkDir(root);
        }
        catch (OperationCanceledException) { Console.WriteLine("Cancelled."); }
        finally { output.Writer.Complete(); }
    });

    return output;
}
The change is straightforward, we need to handle the cancellation in a try/catch block and not forget to close the output channel. Keep in mind that canceling only the initial stage will leave the next stages running with the existing jobs which might not always be desired, especially if the stages are long-running. Therefore, we have to make them able to handle cancellation as well.

var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(2));
var fileSource = GetFilesRecursively("path_to/node_modules", cts.Token);
...
Dealing with Backpressure
The term backpressure is borrowed from fluid dynamics and relates to to the software systems' dataflow. In our examples, the stages execute concurrently but this doesn’t guarantee an optimal performance. Let’s revisit the pizza example. It takes a longer time to bake the pizza than to add the toppings. This becomes an issue when we have to process a large number of pizza orders as we’re going to end up with many pizzas with their toppings added, waiting to be baked, but our oven bakes only one at a time. We can solve this by getting a larger oven, or even multiple ovens.



In the line-counter example, the stage where we read the file and count its lines might cause might experience backpressure because reading a sufficiently large file (stage 2) is slower than retreiving a file metadata (stage 1). It makes sense to increase the capacity of this stage and that’s where Split<T> and Merge<T> which we discussed in part 1 come into use. We’ll summarize them.

Split
Split<T> is a method that takes an input channel and distributes its messages amongst several outputs. That way we can let several threads concurrently handle the message processing.



Expand Split<T> implementation

We’re going to use it to distribute the source code files among 5 channels which will let us process up to 5 files simultaneously. This is similar to when supermarkets open additional checkout lines when there’re a lot of customers waiting.

var fileSource = GetFilesRecursively("path_to/node_modules");
var sourceCodeFiles =
    FilterByExtension(fileSource, new HashSet<string> {".js", ".ts" });
var splitter = Split(sourceCodeFiles, 5);
Merge
Merge<T> is the opposite operation. It takes multiple input channels and consolidates them in a single output.



Expand Merge<T> implementation

During Merge<T>, we read concurrently from several channels, so this is the stage that we need to tweak a little bit and perform the line counting.

We introduce, CountLinesAndMerge which doesn’t only redirect, but also transforms.

ChannelReader<(FileInfo file, int lines)>
    CountLinesAndMerge(IList<ChannelReader<FileInfo>> inputs)
{
    var output = Channel.CreateUnbounded<(FileInfo file, int lines)>();

    Task.Run(async () =>
    {
        async Task Redirect(ChannelReader<FileInfo> input)
        {
            await foreach (var file in input.ReadAllAsync())
                await output.Writer.WriteAsync((file, CountLines(file)));
        }
            
        await Task.WhenAll(inputs.Select(Redirect).ToArray());
        output.Writer.Complete();
    });

    return output;
}
The error handling and the cancellation are omitted for the sake of brevity, however, we’ve already seen how to implement them. Now we’re ready to build our pipeline.

var fileSource = GetFilesRecursively("path_to/node_modules");
var sourceCodeFiles =
    FilterByExtension(fileSource, new HashSet<string> {".js", ".ts"});
- var counter = GetLineCount(sourceCodeFiles);
+ var counter = CountLinesAndMerge(Split(sourceCodeFiles, 5));
TPL Dataflow
The TPL Dataflow library is another option for implementing pipelines or even meshes in .NET. It has a powerful, high-level API but compared to the channel-based approach also comes with a steeper learning curve and provides less control. Personally, I think that deciding between the two should strongly depend on the case. If you prefer a simpler API and more control, the lightweight channels would be the way to go. If you want a high-level API with more features, check out TPL Dataflow.

Conclusion
We defined the pipeline concurrency model and learned how to use it to implement flexible, high-performance data processing workflows. We learned how to deal with errors, perform cancellation as well as how to apply some of the channel techniques (multiplexing and demultiplexing), described in the previous articles, to handle backpressure.

Besides performance, pipelines are also easy to change. Each stage is an atomic part of the composition that can be independently modified, replaced, or removed as long as we keep the method (stage) signatures intact. For example, it’s trivial to convert the line counter pipeline to search for patterns in text, say parsing log files etc. by replacing the line counter stage with another one.

We can see how it can lead to a significant reduction in our code’s cyclomatic complexity as well as making it easier to test. Each stage is simply a method with no side effects, which can be unit tested in isolation. Stages have a single responsibility, which makes them easier to reason about, thus we can cover all the possible cases.

Let’s go the other way around and tell Joe to stop talking. We need to modify our CreateMessenger<T>() generator so it supports cancellation.

static ChannelReader<string> CreateMessenger(
    string msg, int count, CancellationToken token = default)
{
    var ch = Channel.CreateUnbounded<string>();

    Task.Run(async () =>
    {
        var rnd = new Random();
        for (int i = 0; i < count; i++)
        {
            if (token.IsCancellationRequested)
            {
                await ch.Writer.WriteAsync($"{msg} says bye!");
                break;
            }
            await ch.Writer.WriteAsync($"{msg} {i}");
            await Task.Delay(TimeSpan.FromSeconds(rnd.Next(0, 3)));
        }
        ch.Writer.Complete();
    });

    return ch.Reader;
}
Now we need to pass our cancellation token to the generator which gives us control over the channel’s longevity.

var cts = new CancellationTokenSource();
var joe = CreateMessenger("Joe", 10, cts.Token);
cts.CancelAfter(TimeSpan.FromSeconds(5));

await foreach (var item in joe.ReadAllAsync())
    Console.WriteLine(item);
Joe had 10 messages to send, but we gave him only 5 seconds, for which he managed to send only 4. We can also manually send a cancellation request, for example, after reading N number of messages.

Joe 0
Joe 1
Joe 2
Joe 3
Joe says bye!
Web Search
We’re given the task to query several data sources and mix the results. The queries should run concurrently and we should disregard the ones taking too long. Also, we should handle a query response at the time of arrival, instead of waiting for all of them to complete. Let’s see how we can solve this using what we’ve learned so far:

var ch = Channel.CreateUnbounded<string>();
We create a local function that simulates a remote async API call and writes its result to the channel.

async Task Search(string source, string term, CancellationToken token)
{
    await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5)), token);
    await ch.Writer.WriteAsync($"Result from {source} for {term}", token);
}
Now we query several data sources by passing a search term and a cancellation token.

var term = "Milky Way";
var token = new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token;

var search1 = Search("Google", term, token);
var search2 = Search("Quora", term, token);
var search3 = Search("Wikipedia", term, token);
We wait and process the results one at a time. We break from the loop once the cancellation kicks in.

try
{
    for (int i = 0; i < 3; i++)
        Console.WriteLine(await ch.Reader.ReadAsync(token));

    Console.WriteLine("All searches have completed.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Timeout.");
}

ch.Writer.Complete();
Depending on the timeout interval we might end up receiving responses for all of the queries,

[9:09:14 AM] Result from Google for Milky Way
[9:09:14 AM] Result from Wikipedia for Milky Way
[9:09:16 AM] Result from Quora for Milky Way
All searches have completed.
or cut off the ones that are too slow.

[9:09:19 AM] Result from Quora for Milky Way
[9:09:20 AM] Result from Wikipedia for Milky Way
Timeout.
Again - our code is non-blocking concurrent, thus there’s no need to use locks, callbacks or any kind of conditional statements.

