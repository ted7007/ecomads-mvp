# 1. Этап сборки frontend
FROM node:20-alpine AS client-build
WORKDIR /src/Ecomads.WebApplication/ClientApp

COPY ["Ecomads.WebApplication/ClientApp/package.json", "Ecomads.WebApplication/ClientApp/package-lock.json", "./"]
RUN npm ci

COPY ["Ecomads.WebApplication/ClientApp/", "./"]
RUN mkdir -p ../wwwroot && npm run build

# 2. Этап сборки backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY ["Ecomads.WebApplication/Ecomads.WebApplication.csproj", "Ecomads.WebApplication/"]
RUN dotnet restore "Ecomads.WebApplication/Ecomads.WebApplication.csproj"

# Копируем остальной исходный код и собираем
COPY . .
RUN rm -rf Ecomads.WebApplication/wwwroot
COPY --from=client-build /src/Ecomads.WebApplication/wwwroot/app ./Ecomads.WebApplication/wwwroot/app
WORKDIR "/src/Ecomads.WebApplication"
RUN dotnet publish "Ecomads.WebApplication.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 3. Этап исполнения (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Указываем порт
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Ecomads.WebApplication.dll"]
