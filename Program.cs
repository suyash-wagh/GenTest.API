using GenTest.Models.TestExecution;
using gentest.Services;
using GenTest.Services;
using gentest.Services.ApiParsing;
using GenTest.Services.ApiParsing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register custom services
builder.Services.AddScoped<ISwaggerFileService, SwaggerFileService>();
builder.Services.AddScoped<ITestGenerationService, TestGenerationService>();
builder.Services.AddScoped<ITestExecutionService, TestExecutionService>();
builder.Services.AddScoped<ITestCaseExtractionService, TestCaseExtractionService>();
builder.Services.AddScoped<IApiDefinitionParser, SwaggerParser>();
builder.Services.AddHttpClient();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.Configure<TestExecutorSettings>(builder.Configuration.GetSection("TestExecutorSettings"));

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", builder => {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.MapControllers();
app.Run();

