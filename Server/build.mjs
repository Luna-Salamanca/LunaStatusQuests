#!/usr/bin/env node

/**
 * Build Script for LunaStatusQuests Mod
 *
 * This script automates the build process for the LunaStatusQuests server-side SPT mod project.
 * It performs the following operations:
 * - Compiles TypeScript to JavaScript
 * - Loads the .buildignore file for ignored files
 * - Loads package.json for project details
 * - Creates a distribution directory
 * - Copies files while respecting .buildignore rules
 * - Creates a zip archive of the project files
 * - Copies to SPT installation for immediate use
 * - Copies to dev dist folder for testing
 * - Cleans up temporary directories
 *
 * Usage:
 * - Run this script using npm: `npm run build`
 * - Use `npm run buildinfo` for detailed logging: `npm run buildinfo`
 *
 * @author LunaStatusQuests
 * @version v2.0.0
 */

import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import fs from "fs-extra";
import ignore from "ignore";
import archiver from "archiver";
import winston from "winston";
import { execSync } from "node:child_process";

// Get command line arguments to determine verbosity
const args = process.argv.slice(2);
const verbose = args.includes("--verbose") || args.includes("-v");

// Configure Winston logger with colors
const logColors = {
    error: "red",
    warn: "yellow",
    info: "grey",
    success: "green"
};
winston.addColors(logColors);

const logger = winston.createLogger({
    levels: {
        error: 0,
        warn: 1,
        success: 2,
        info: 3
    },
    format: winston.format.combine(
        winston.format.colorize(),
        winston.format.printf((info) => `${info.level}: ${info.message}`)
    ),
    transports: [
        new winston.transports.Console({
            level: verbose ? "info" : "success"
        })
    ]
});

// Deployment configuration
const getDeploymentPaths = (projectName) => ({
    spt: `G:\\SPT11\\SPT 3.11\\user\\mods\\${projectName}`,
    devDist: `G:\\SPT11\\SPT 3.11\\SPT 3.11 DEV\\LunaStatusQuests\\dist\\user\\mods\\${projectName}`
});

/**
 * Main build orchestration function
 */
async function main() 
{
    const currentDir = getCurrentDirectory();
    let projectDir;

    try 
    {
        // Step 1: Compile TypeScript to JavaScript
        logStep("Compiling TypeScript...");
        compileTypeScript(currentDir);
        logSuccess("TypeScript compiled successfully.");

        // Step 2: Load build configuration
        const buildIgnorePatterns = await loadBuildIgnoreFile(currentDir);
        const packageJson = await loadPackageJson(currentDir);

        // Step 3: Create project name and directories
        const projectName = createProjectName(packageJson);
        logSuccess(`Project name: ${projectName}`);

        const distDir = await removeOldDistDirectory(currentDir);
        logStep("Distribution directory cleaned.");

        projectDir = await createTemporaryDirectoryWithProjectName(projectName);
        logSuccess("Temporary working directory created.");
        logInfo(projectDir);

        // Step 4: Copy files respecting .buildignore
        logStep("Copying files (respecting .buildignore)...");
        await copyFiles(currentDir, projectDir, buildIgnorePatterns);
        logSuccess("Files copied to temporary directory.");

        // Step 5: Create ZIP archive
        logStep("Creating ZIP archive...");
        const zipFilePath = path.join(path.dirname(projectDir), `${projectName}.zip`);
        await createZipFile(projectDir, zipFilePath, `user/mods/${projectName}`);
        logSuccess("Archive created successfully.");
        logInfo(zipFilePath);

        // Step 5B & 5C: Deploy to configured locations
        const deployPaths = getDeploymentPaths(projectName);
        await deployMod(projectDir, deployPaths.spt, "SPT installation");
        await deployMod(projectDir, deployPaths.devDist, "dev dist folder");

        // Step 6: Move archive to dist
        const zipFileInDist = path.join(distDir, `${projectName}.zip`);
        await fs.move(zipFilePath, zipFileInDist);
        logSuccess("Archive moved to distribution directory.");

        // Success message
        logSeparator();
        logSuccess("Build completed successfully!");
        logSuccess("Mod package location:");
        logSuccess(`dist/${projectName}.zip`);
        logSuccess("SPT installation:");
        logSuccess(deployPaths.spt);
        logSuccess("Dev dist location:");
        logSuccess(deployPaths.devDist);
        logSeparator();
        
        if (!verbose) 
        {
            logSuccess("For detailed logs, use: npm run buildinfo");
        }
    }
    catch (err) 
    {
        logError(`Build failed: ${err.message}`);
        process.exit(1);
    }
    finally 
    {
        // Cleanup temporary directory
        if (projectDir) 
        {
            try 
            {
                await fs.promises.rm(projectDir, { force: true, recursive: true });
                logInfo("Cleaned temporary directory.");
            }
            catch (err) 
            {
                logError(`Failed to clean temporary directory: ${err.message}`);
            }
        }
    }
}

/**
 * Deploy mod to a specific location (clean, ensure directory, copy files)
 */
async function deployMod(sourceDir, destPath, description) 
{
    logStep(`Deploying to ${description}...`);
    
    if (await fs.pathExists(destPath)) 
    {
        await fs.remove(destPath);
        logInfo(`Cleaned old mod files from ${description}.`);
    }
    
    await fs.ensureDir(destPath);
    await fs.copy(sourceDir, destPath);
    logSuccess(`Files deployed to ${description}.`);
    logInfo(destPath);
}

/**
 * Logger helper functions
 */
const logStep = (msg) => logger.log("info", msg);
const logSuccess = (msg) => logger.log("success", msg);
const logInfo = (msg) => logger.log("info", msg);
const logError = (msg) => logger.log("error", msg);
const logWarn = (msg) => logger.log("warn", msg);
const logSeparator = () => logger.log("success", "------------------------------------");

/**
 * Get current directory where script is running
 */
function getCurrentDirectory() 
{
    return path.dirname(fileURLToPath(import.meta.url));
}

/**
 * Compile TypeScript to JavaScript
 */
function compileTypeScript(currentDir) 
{
    try 
    {
        execSync("npx tsc", { cwd: currentDir, stdio: "inherit" });
    }
    catch (err) 
    {
        throw new Error("TypeScript compilation failed");
    }
}

/**
 * Load .buildignore patterns
 */
async function loadBuildIgnoreFile(currentDir) 
{
    const buildIgnorePath = path.join(currentDir, ".buildignore");

    try 
    {
        const fileContent = await fs.promises.readFile(buildIgnorePath, "utf-8");
        return ignore().add(fileContent.split("\n"));
    }
    catch (err) 
    {
        logWarn("No .buildignore file found. Ignoring default patterns.");
        return ignore();
    }
}

/**
 * Load package.json
 */
async function loadPackageJson(currentDir) 
{
    const packageJsonPath = path.join(currentDir, "package.json");
    const packageJsonContent = await fs.promises.readFile(packageJsonPath, "utf-8");
    return JSON.parse(packageJsonContent);
}

/**
 * Create project name from package.json details
 */
function createProjectName(packageJson) 
{
    const author = (packageJson.author || "Unknown").replace(/\W/g, "");
    const name = (packageJson.name || "mod").replace(/\W/g, "");
    return `${author}-${name}`.toLowerCase();
}

/**
 * Remove old dist directory
 */
async function removeOldDistDirectory(projectDir) 
{
    const distPath = path.join(projectDir, "dist");
    await fs.remove(distPath);
    return distPath;
}

/**
 * Create temporary directory with project name
 */
async function createTemporaryDirectoryWithProjectName(projectName) 
{
    const tempDir = await fs.promises.mkdtemp(path.join(os.tmpdir(), "spt-mod-"));
    const projectDir = path.join(tempDir, projectName);
    await fs.ensureDir(projectDir);
    return projectDir;
}

/**
 * Copy files respecting .buildignore patterns
 */
async function copyFiles(srcDir, destDir, ignoreHandler) 
{
    try 
    {
        const entries = await fs.promises.readdir(srcDir, { withFileTypes: true });
        const copyOperations = [];

        for (const entry of entries) 
        {
            const srcPath = path.join(srcDir, entry.name);
            const destPath = path.join(destDir, entry.name);
            const relativePath = path.relative(process.cwd(), srcPath);

            if (ignoreHandler.ignores(relativePath)) 
            {
                logInfo(`Ignored: ${relativePath}`);
                continue;
            }

            if (entry.isDirectory()) 
            {
                await fs.ensureDir(destPath);
                copyOperations.push(copyFiles(srcPath, destPath, ignoreHandler));
            }
            else 
            {
                copyOperations.push(
                    fs.copy(srcPath, destPath).then(() => 
                    {
                        logInfo(`Copied: ${relativePath}`);
                    })
                );
            }
        }

        await Promise.all(copyOperations);
    }
    catch (err) 
    {
        throw new Error("Error copying files: " + err.message);
    }
}

/**
 * Create ZIP archive of project files
 */
async function createZipFile(directoryToZip, zipFilePath, containerDirName) 
{
    return new Promise((resolve, reject) => 
    {
        const output = fs.createWriteStream(zipFilePath);
        const archive = archiver("zip", { zlib: { level: 9 } });

        output.on("close", () => 
        {
            logInfo("Archive finalized successfully.");
            resolve();
        });

        archive.on("warning", (err) => 
        {
            if (err.code === "ENOENT") 
            {
                logWarn(`Archiver warning: ${err.code}`);
            }
            else 
            {
                reject(err);
            }
        });

        archive.on("error", reject);

        archive.pipe(output);
        archive.directory(directoryToZip, containerDirName);
        archive.finalize();
    });
}

// Run the build
main();