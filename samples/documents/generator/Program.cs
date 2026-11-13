using Broadside.SampleDocuments;

// Writes one of the Broadside-made sample documents: Program <samples/documents folder> <document> <output folder>.
if (args.Length != 3)
{
    Console.Error.WriteLine("usage: Broadside.SampleDocuments <samples/documents folder> <document> <output folder>");
    Console.Error.WriteLine($"documents: {string.Join(", ", Documents.Names)}");
    return 2;
}

var context = new GeneratorContext(Path.GetFullPath(args[0]), Path.GetFullPath(args[2]));
Directory.CreateDirectory(context.Output);
if (!Documents.TryRun(args[1], context))
{
    Console.Error.WriteLine($"unknown document '{args[1]}'; known: {string.Join(", ", Documents.Names)}");
    return 2;
}

return 0;
