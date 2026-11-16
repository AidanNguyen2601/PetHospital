@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === Trien khai PetHospital - Phuong an A (SQL Server 2022 rieng) ===
if not exist .env copy .env.example .env >nul
docker compose up -d --build
echo.
echo Mo trinh duyet: http://localhost:8160  (admin / Admin@123) - lan dau doi 1-2 phut de khoi tao du lieu
pause >nul
