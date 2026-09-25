# Imagem de produção do Prisma: a API ASP.NET servindo o React no mesmo domínio (PLAN.md, H.1).
# Build local: docker build -t prisma .

# 1. Frontend: tipos checados e build do Vite em dist/.
FROM node:24-alpine AS web
WORKDIR /web
COPY src/prisma-web/package.json src/prisma-web/package-lock.json ./
RUN npm ci
COPY src/prisma-web/ ./
RUN npm run build

# 2. API: restore separado do código, para o cache das camadas sobreviver a mudanças no código.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Prisma.Domain/Prisma.Domain.csproj src/Prisma.Domain/
COPY src/Prisma.Api/Prisma.Api.csproj src/Prisma.Api/
RUN dotnet restore src/Prisma.Api/Prisma.Api.csproj
COPY src/Prisma.Domain/ src/Prisma.Domain/
COPY src/Prisma.Api/ src/Prisma.Api/
RUN dotnet publish src/Prisma.Api/Prisma.Api.csproj -c Release -o /app --no-restore

# 3. Imagem final: só o runtime, a API publicada e o React em wwwroot, rodando sem root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
# Produção atrás do proxy da hospedagem: IP real do cliente e migrations ao subir. A connection
# string vem da variável ConnectionStrings__Default, nunca da imagem.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ForwardedHeaders__Enabled=true \
    Database__MigrateOnStartup=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Prisma.Api.dll"]
