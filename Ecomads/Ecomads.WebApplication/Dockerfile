# 1. Этап сборки (Build)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY ["Ecomads.WebApplication/Ecomads.WebApplication.csproj", "Ecomads.WebApplication/"]
RUN dotnet restore "Ecomads.WebApplication/Ecomads.WebApplication.csproj"

# Копируем остальной исходный код и собираем
COPY . .
WORKDIR "/src/Ecomads.WebApplication"
RUN dotnet publish "Ecomads.WebApplication.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Этап исполнения (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Указываем порт
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Ecomads.WebApplication.dll"]
