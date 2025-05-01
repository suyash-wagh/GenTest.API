using gentest.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register custom services
builder.Services.AddScoped<ISwaggerFileService, SwaggerFileService>();
builder.Services.AddScoped<ITestGenerationService, TestGenerationService>();
builder.Services.AddScoped<ITestExecutionService, TestExecutionService>();
builder.Services.AddScoped<ITestCaseExtractionService, TestCaseExtractionService>();
builder.Services.AddHttpClient(); // Add this line to register IHttpClientFactory
builder.Services.AddControllers();
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

