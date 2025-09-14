using System.Text.Json;
using System.Text.Json.Serialization;

public class JsonStore<TRoot> where TRoot : class, new()
{
	private readonly string _path;
	private readonly SemaphoreSlim _mutex = new(1, 1);

	private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public JsonStore(string path, TRoot defaultValue)
	{
		_path = path;
		if (!File.Exists(_path)) SaveAsync(defaultValue).GetAwaiter().GetResult();
	}

	public async Task<TRoot> LoadAsync()
	{
		await _mutex.WaitAsync();
		try
		{
			var text = await File.ReadAllTextAsync(_path);
			return JsonSerializer.Deserialize<TRoot>(text, _json) ?? new TRoot();
		}
		finally { _mutex.Release(); }
	}

	public async Task SaveAsync(TRoot data)
	{
		await _mutex.WaitAsync();
		try
		{
			var tmp = _path + ".tmp";
			var json = JsonSerializer.Serialize(data, _json);
			await File.WriteAllTextAsync(tmp, json);
			File.Move(tmp, _path, overwrite: true);
		}
		finally { _mutex.Release(); }
	}
}