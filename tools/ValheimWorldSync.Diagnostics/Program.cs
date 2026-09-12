using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Storage;

if (args.Length != 2 || args[0] is not ("status" or "cas-test"))
{
    Console.WriteLine("Uso: diagnostics status <config.json> | cas-test <config-de-bucket-de-teste.json>");
    return 2;
}
try
{
    var config = await AppConfiguration.LoadAsync(Path.GetFullPath(args[1]));
    config.Validate();
    var prefix = args[0] == "cas-test" ? $"vws-tests/{Guid.NewGuid():N}/" : "";
    using var repo = new R2WorldRepository(config, prefix);
    if (args[0] == "status")
    {
        var read = await repo.ReadAsync();
        Console.WriteLine(read is null ? "Mundo não inicializado." :
            $"Versão: {read.Manifest.Current?.Id ?? "nenhuma"}; posse: {read.Manifest.Lease?.Player ?? "livre"}");
    }
    else
    {
        var manifest = new WorldManifest { WorldId = config.WorldId };
        var results = await Task.WhenAll(
            repo.TryWriteAsync(manifest, null),
            repo.TryWriteAsync(manifest with { Revision = Guid.NewGuid().ToString("N") }, null));
        var winner = results.Single(r => r is not null)!;
        var update = await repo.TryWriteAsync(winner.Manifest with { Revision = Guid.NewGuid().ToString("N") }, winner.ETag);
        if (update is null || await repo.TryWriteAsync(manifest, winner.ETag) is not null) throw new InvalidOperationException("CAS incorreto.");
        Console.WriteLine($"CAS confirmado. Evidência preservada no prefixo {prefix}");
    }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Falha no diagnóstico ({e.GetType().Name}). Verifique configuração, rede e acesso ao bucket.");
    return 1;
}
