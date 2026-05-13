# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["taskup-backend.csproj", "./"]
RUN dotnet restore "taskup-backend.csproj"
COPY . .
RUN dotnet publish "taskup-backend.csproj" -c Release -o /app/publish

# Run Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

# Render dynamically assigns a port, we tell ASP.NET to listen to it
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "taskup-backend.dll"]